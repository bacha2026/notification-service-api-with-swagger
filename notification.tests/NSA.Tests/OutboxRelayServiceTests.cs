extern alias outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using IOutboxMessagePublisher = outbox::NSA.Application.Abstractions.IOutboxMessagePublisher;
using OutboxMessage = outbox::NSA.Domain.Entities.OutboxMessage;
using OutboxOptions = outbox::NSA.Infrastructure.Configuration.OutboxOptions;
using NotificationDbContext = outbox::NSA.Persistence.NotificationDbContext;
using OutboxRelayService = outbox::NSA.Outbox.Worker.OutboxRelayService;
using OutboxRelayWorker = outbox::NSA.Outbox.Worker.OutboxRelayWorker;

namespace NSA.Tests;

public sealed class OutboxRelayServiceTests
{
    [Fact]
    public async Task Relay_claims_publishes_and_marks_a_message_with_its_stable_id()
    {
        var now = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var message = CreateMessage(now);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var publisher = new RecordingOutboxPublisher();
        var relay = CreateRelay(context, publisher, now);

        var result = await relay.RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.Failed);
        Assert.Equal(message.Id, Assert.Single(publisher.Messages).Id);
        var persisted = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(now, persisted.PublishedAtUtc);
        Assert.Null(persisted.LockedBy);
        Assert.Null(persisted.LockedUntilUtc);
    }

    [Fact]
    public async Task Relay_releases_a_failed_claim_and_schedules_bounded_backoff()
    {
        var now = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var message = CreateMessage(now);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var relay = CreateRelay(
            context,
            new RecordingOutboxPublisher(new IOException("broker unavailable")),
            now);

        var result = await relay.RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Failed);
        var persisted = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(persisted.PublishedAtUtc);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.True(persisted.NextAttemptAtUtc > now);
        Assert.True(persisted.NextAttemptAtUtc <= now.AddSeconds(300));
        Assert.Null(persisted.LockedBy);
        Assert.Contains(nameof(IOException), persisted.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("broker unavailable", persisted.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Relay_does_not_steal_an_unexpired_lease()
    {
        var now = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var message = CreateMessage(now);
        message.LockedBy = "relay-a";
        message.LockedUntilUtc = now.AddMinutes(1);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var publisher = new RecordingOutboxPublisher();
        var relay = CreateRelay(context, publisher, now);

        var result = await relay.RelayDueAsync("relay-b", CancellationToken.None);

        Assert.Equal(0, result.Claimed);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task Empty_backlog_does_not_report_ready_when_the_broker_probe_fails()
    {
        var readinessFile = Path.Combine(
            Path.GetTempPath(),
            $"nsa-outbox-readiness-{Guid.NewGuid():N}");
        var publisher = new RecordingOutboxPublisher(
            new IOException("broker unavailable"));
        var options = new OutboxOptions
        {
            BatchSize = 10,
            PollIntervalSeconds = 1,
            LeaseSeconds = 30,
            MaximumBackoffSeconds = 300
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NotificationDbContext>(builder =>
            builder.UseInMemoryDatabase($"outbox-readiness-{Guid.NewGuid():N}"));
        services.AddSingleton<IOutboxMessagePublisher>(publisher);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OutboxRelayService>();
        await using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:ReadinessFile"] = readinessFile
            })
            .Build();
        var worker = new OutboxRelayWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            options,
            configuration,
            NullLogger<OutboxRelayWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => publisher.HealthCheckCount > 0);

        Assert.False(File.Exists(readinessFile));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Broker_outage_records_backoff_for_due_rows_without_an_extra_health_probe()
    {
        var readinessFile = Path.Combine(
            Path.GetTempPath(),
            $"nsa-outbox-readiness-{Guid.NewGuid():N}");
        var publisher = new RecordingOutboxPublisher(
            new IOException("broker unavailable"));
        var options = new OutboxOptions
        {
            BatchSize = 10,
            PollIntervalSeconds = 1,
            LeaseSeconds = 30,
            MaximumBackoffSeconds = 300
        };
        var databaseName = $"outbox-outage-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NotificationDbContext>(builder =>
            builder.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IOutboxMessagePublisher>(publisher);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OutboxRelayService>();
        await using var provider = services.BuildServiceProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            dbContext.OutboxMessages.Add(CreateMessage(DateTimeOffset.UtcNow.AddSeconds(-1)));
            await dbContext.SaveChangesAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:ReadinessFile"] = readinessFile
            })
            .Build();
        var worker = new OutboxRelayWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            options,
            configuration,
            NullLogger<OutboxRelayWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => publisher.Messages.Count > 0);
        await worker.StopAsync(CancellationToken.None);

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var persisted = await verification.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, persisted.AttemptCount);
        Assert.True(persisted.NextAttemptAtUtc > persisted.OccurredAtUtc);
        Assert.Null(persisted.LockedBy);
        Assert.Equal(0, publisher.HealthCheckCount);
        Assert.False(File.Exists(readinessFile));
    }

    private static NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"outbox-relay-{Guid.NewGuid():N}")
            .Options);

    private static OutboxRelayService CreateRelay(
        NotificationDbContext context,
        IOutboxMessagePublisher publisher,
        DateTimeOffset now) =>
        new(
            context,
            publisher,
            new OutboxOptions
            {
                BatchSize = 10,
                PollIntervalSeconds = 1,
                LeaseSeconds = 30,
                MaximumBackoffSeconds = 300
            },
            new ManualTimeProvider(now),
            NullLogger<OutboxRelayService>.Instance);

    private static OutboxMessage CreateMessage(DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AggregateType = "BulkNotificationJob",
            AggregateId = Guid.NewGuid().ToString("D"),
            MessageType = "test.message.v1",
            SchemaVersion = 1,
            Payload = "{}",
            Exchange = "test.exchange",
            RoutingKey = "test.route",
            CorrelationId = "test-correlation",
            OccurredAtUtc = now,
            NextAttemptAtUtc = now
        };

    private sealed class RecordingOutboxPublisher(Exception? exception = null)
        : IOutboxMessagePublisher
    {
        public List<OutboxMessage> Messages { get; } = [];
        public int HealthCheckCount { get; private set; }

        public Task CheckHealthAsync(CancellationToken cancellationToken)
        {
            HealthCheckCount++;
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return exception is null
                ? Task.CompletedTask
                : Task.FromException(exception);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
