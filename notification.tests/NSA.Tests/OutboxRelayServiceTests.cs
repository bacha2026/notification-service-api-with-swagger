extern alias outbox;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using NotificationFailureKind = outbox::NSA.Outbox.Worker.NotificationFailureKind;
using BulkNotificationJob = outbox::NSA.Outbox.Worker.BulkNotificationJob;
using BulkNotificationJobItem = outbox::NSA.Outbox.Worker.BulkNotificationJobItem;
using BulkNotificationRequestedV1 = outbox::NSA.Outbox.Worker.BulkNotificationRequestedV1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using IOutboxMessagePublisher = outbox::NSA.Outbox.Worker.IOutboxMessagePublisher;
using OutboxMessage = outbox::NSA.Outbox.Worker.OutboxMessage;
using OutboxOptions = outbox::NSA.Outbox.Worker.OutboxOptions;
using NotificationDbContext = outbox::NSA.Outbox.Worker.NotificationDbContext;
using OutboxService = outbox::NSA.Outbox.Worker.OutboxService;
using OutboxWorker = outbox::NSA.Outbox.Worker.OutboxWorker;

namespace NSA.Tests;

public sealed class OutboxRelayServiceTests : IDisposable
{
    private readonly SqliteTestDatabase<NotificationDbContext> database = new();
    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Database_timeout_loading_job_routes_the_loaded_command_to_retry()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>(database.Options)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new JobQueryTimeoutInterceptor()).Options);
        SeedMessage(context, CreateMessage(now));
        await context.SaveChangesAsync();
        var publisher = new RecordingOutboxPublisher();

        var result = await CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Empty(publisher.Messages);
        Assert.Equal(NotificationFailureKind.Timeout, Assert.Single(publisher.Failures));
        Assert.NotNull((await context.OutboxMessages.AsNoTracking().SingleAsync()).PublishedAtUtc);
    }

    [Fact]
    public async Task Terminal_status_database_failure_retains_the_outbox_without_publishing_to_dead_letter()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>(database.Options)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new JobQueryTimeoutInterceptor()).Options);
        var message = CreateMessage(now);
        message.Payload = "{";
        SeedMessage(context, message);
        await context.SaveChangesAsync();
        var publisher = new RecordingOutboxPublisher();

        var result = await CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Empty(publisher.Messages);
        Assert.Empty(publisher.Failures);
        var pending = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(pending.PublishedAtUtc);
        Assert.Null(pending.LockedBy);
        Assert.True(pending.NextAttemptAtUtc > now);
        await using var verification = CreateContext();
        Assert.Equal("Queued", (await verification.BulkNotificationJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Failed_dead_letter_publication_retains_the_original_payload_in_the_outbox()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var message = CreateMessage(now);
        message.Payload = "{";
        SeedMessage(context, message);
        await context.SaveChangesAsync();
        var publisher = new RecordingOutboxPublisher(new IOException());

        var result = await CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Equal(NotificationFailureKind.Permanent, Assert.Single(publisher.Failures));
        var persisted = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(persisted.PublishedAtUtc);
        Assert.Equal("{", persisted.Payload);
        Assert.True(persisted.NextAttemptAtUtc > now);
        Assert.Equal("DeadLettered", (await context.BulkNotificationJobs.AsNoTracking().SingleAsync()).Status);
    }

    private sealed class JobQueryTimeoutInterceptor : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
            new JobQueryVisitor().Visit(queryExpression);

        private sealed class JobQueryVisitor : ExpressionVisitor
        {
            protected override Expression VisitExtension(Expression node)
            {
                if (node is EntityQueryRootExpression root && root.EntityType.ClrType == typeof(BulkNotificationJob))
                    throw new TimeoutException("SQL job query timed out.");
                return base.VisitExtension(node);
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("envelope")]
    [InlineData("items")]
    [InlineData("recipient")]
    [InlineData("order")]
    [InlineData("job")]
    public async Task Permanent_failure_is_confirmed_to_dead_letter_before_the_row_is_marked_published(string failure)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var message = CreateMessage(now);
        SeedMessage(context, message);
        await context.SaveChangesAsync();
        var job = await context.BulkNotificationJobs.Include(job => job.Items).SingleAsync();
        switch (failure)
        {
            case "json": message.Payload = "{"; break;
            case "empty": message.Payload = ""; break;
            case "null": message.Payload = "null"; break;
            case "envelope": message.Payload = "{}"; break;
            case "items": context.RemoveRange(job.Items); break;
            case "recipient": job.Items.First().RecipientEmail = ""; break;
            case "order": job.Items.First().OrderId = 999; break;
            case "job": context.Remove(job); break;
        }
        await context.SaveChangesAsync();
        var publisher = new RecordingOutboxPublisher();

        var result = await CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Empty(publisher.Messages);
        Assert.Equal(NotificationFailureKind.Permanent, Assert.Single(publisher.Failures));
        Assert.NotNull((await context.OutboxMessages.AsNoTracking().SingleAsync()).PublishedAtUtc);
        if (failure != "job")
        {
            var persistedJob = await context.BulkNotificationJobs.AsNoTracking().SingleAsync();
            Assert.Equal("DeadLettered", persistedJob.Status);
            Assert.Equal(now, persistedJob.CompletedAtUtc);
        }
    }

    [Theory]
    [InlineData("timeout", NotificationFailureKind.Timeout)]
    [InlineData("http-timeout", NotificationFailureKind.Timeout)]
    [InlineData("database-timeout", NotificationFailureKind.Timeout)]
    [InlineData("canceled-request", NotificationFailureKind.Timeout)]
    [InlineData("408", NotificationFailureKind.Timeout)]
    [InlineData("504", NotificationFailureKind.Timeout)]
    [InlineData("429", NotificationFailureKind.Backoff)]
    [InlineData("503", NotificationFailureKind.Backoff)]
    [InlineData("bug", NotificationFailureKind.Unknown)]
    [InlineData("unknown", NotificationFailureKind.Unknown)]
    [InlineData("permanent", NotificationFailureKind.Permanent)]
    public async Task Publication_failure_is_classified_and_handed_to_its_destination(string failure, NotificationFailureKind expected)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        SeedMessage(context, CreateMessage(now));
        await context.SaveChangesAsync();
        Exception exception = failure switch
        {
            "timeout" => new TimeoutException(),
            "http-timeout" => new HttpRequestException("request timed out", new TimeoutException()),
            "database-timeout" => new DbUpdateException("database timed out", new TimeoutException()),
            "canceled-request" => new TaskCanceledException(),
            "408" => new HttpRequestException("request timed out", null, System.Net.HttpStatusCode.RequestTimeout),
            "504" => new HttpRequestException("gateway timed out", null, System.Net.HttpStatusCode.GatewayTimeout),
            "429" => new HttpRequestException("throttled", null, System.Net.HttpStatusCode.TooManyRequests),
            "503" => new HttpRequestException("unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable),
            "permanent" => new outbox::NSA.Outbox.Worker.PermanentMessageException("invalid business state"),
            "unknown" => new Exception("unknown failure"),
            _ => new NullReferenceException()
        };
        var publisher = new RecordingOutboxPublisher(exception) { FailRouting = false };

        var result = await CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Equal(expected, Assert.Single(publisher.Failures));
        Assert.Single(publisher.Messages);
        Assert.NotNull((await context.OutboxMessages.AsNoTracking().SingleAsync()).PublishedAtUtc);
        var job = await context.BulkNotificationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(expected == NotificationFailureKind.Permanent ? "DeadLettered" : "Retrying", job.Status);
        Assert.Equal(expected == NotificationFailureKind.Permanent, job.CompletedAtUtc.HasValue);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("CompletedWithErrors")]
    [InlineData("PublishFailed")]
    [InlineData("DeadLettered")]
    public async Task Failure_handoff_preserves_a_terminal_job(string status)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var message = CreateMessage(now);
        message.Payload = "{";
        SeedMessage(context, message);
        var job = context.BulkNotificationJobs.Local.Single();
        job.Status = status;
        job.CompletedAtUtc = now.AddMinutes(-1);
        job.Error = "existing terminal result";
        await context.SaveChangesAsync();

        var publisher = new RecordingOutboxPublisher();
        var result = await CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Equal(NotificationFailureKind.Permanent, Assert.Single(publisher.Failures));
        var persisted = await context.BulkNotificationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(status, persisted.Status);
        Assert.Equal(now.AddMinutes(-1), persisted.CompletedAtUtc);
        Assert.Equal("existing terminal result", persisted.Error);
    }

    [Fact]
    public async Task Dead_letter_status_is_saved_before_publication_and_row_settles_only_after_confirmation()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var message = CreateMessage(now);
        message.Payload = "{";
        SeedMessage(context, message);
        await context.SaveChangesAsync();
        var publicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationConfirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new RecordingOutboxPublisher
        {
            ConfirmFailure = async () =>
            {
                publicationStarted.SetResult();
                await publicationConfirmed.Task;
            }
        };

        var relayTask = CreateRelay(context, publisher, now).RelayDueAsync("relay-a", CancellationToken.None);
        await publicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null((await context.OutboxMessages.AsNoTracking().SingleAsync()).PublishedAtUtc);
        Assert.Equal("DeadLettered", (await context.BulkNotificationJobs.AsNoTracking().SingleAsync()).Status);

        publicationConfirmed.SetResult();
        Assert.Equal(1, (await relayTask).Published);
        Assert.NotNull((await context.OutboxMessages.AsNoTracking().SingleAsync()).PublishedAtUtc);
        Assert.Equal("DeadLettered", (await context.BulkNotificationJobs.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relay_claims_publishes_and_marks_a_message_with_its_stable_id(bool legacyRouting)
    {
        var now = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var message = CreateMessage(now);
        if (!legacyRouting)
        {
            message.Exchange = string.Empty;
            message.RoutingKey = string.Empty;
        }
        SeedMessage(context, message);
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
        Assert.Empty(publisher.Failures);
        Assert.Equal("Queued", (await context.BulkNotificationJobs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Relay_releases_a_failed_claim_and_schedules_bounded_backoff()
    {
        var now = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var message = CreateMessage(now);
        SeedMessage(context, message);
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
        SeedMessage(context, message);
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
        services.AddSqliteTestDatabase<NotificationDbContext>();
        services.AddSingleton<IOutboxMessagePublisher>(publisher);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OutboxService>();
        await using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:ReadinessFile"] = readinessFile
            })
            .Build();
        var worker = new OutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            options,
            configuration,
            NullLogger<OutboxWorker>.Instance);

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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqliteTestDatabase<NotificationDbContext>();
        services.AddSingleton<IOutboxMessagePublisher>(publisher);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OutboxService>();
        await using var provider = services.BuildServiceProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            SeedMessage(dbContext, CreateMessage(DateTimeOffset.UtcNow.AddSeconds(-1)));
            await dbContext.SaveChangesAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:ReadinessFile"] = readinessFile
            })
            .Build();
        var worker = new OutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            options,
            configuration,
            NullLogger<OutboxWorker>.Instance);

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

    private NotificationDbContext CreateContext() => database.CreateContext();

    private static OutboxService CreateRelay(
        NotificationDbContext context,
        IOutboxMessagePublisher publisher,
        DateTimeOffset now)
    {
        // The production relay starts in a fresh scope. Discard seed tracking so
        // ExecuteUpdate claims are read back from the database before publication.
        context.ChangeTracker.Clear();
        return new(
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
            NullLogger<OutboxService>.Instance);
    }

    private static OutboxMessage CreateMessage(DateTimeOffset now)
    {
        var messageId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        return new()
        {
            Id = messageId,
            AggregateType = "BulkNotificationJob",
            AggregateId = jobId.ToString("D"),
            MessageType = BulkNotificationRequestedV1.MessageType,
            SchemaVersion = 1,
            Payload = JsonSerializer.Serialize(new BulkNotificationRequestedV1(1, messageId, jobId, "test-correlation", now),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Exchange = "test.exchange",
            RoutingKey = "test.route",
            CorrelationId = "test-correlation",
            OccurredAtUtc = now,
            NextAttemptAtUtc = now
        };
    }

    private static void SeedMessage(NotificationDbContext context, OutboxMessage message)
    {
        context.OutboxMessages.Add(message);
        context.BulkNotificationJobs.Add(new BulkNotificationJob
        {
            Id = Guid.Parse(message.AggregateId), Status = "Queued", MessageSchemaVersion = 1,
            CorrelationId = message.CorrelationId, TotalCount = 1, QueuedAtUtc = message.OccurredAtUtc,
            Items = [new BulkNotificationJobItem
            {
                RecipientEmail = "user@example.test", Subject = "subject", Body = "body",
                Channel = outbox::NSA.Outbox.Worker.NotificationChannel.InApp, Status = "Pending"
            }]
        });
    }

    private sealed class RecordingOutboxPublisher(Exception? exception = null)
        : IOutboxMessagePublisher
    {
        public List<OutboxMessage> Messages { get; } = [];
        public List<NotificationFailureKind> Failures { get; } = [];
        public bool FailRouting { get; init; } = true;
        public Func<Task>? ConfirmFailure { get; init; }

        public Task PublishFailureAsync(OutboxMessage message, NotificationFailureKind kind, string errorType, CancellationToken cancellationToken)
        {
            Failures.Add(kind);
            return FailRouting && exception is not null
                ? Task.FromException(exception)
                : ConfirmFailure?.Invoke() ?? Task.CompletedTask;
        }
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
