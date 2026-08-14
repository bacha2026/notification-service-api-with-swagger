using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Service;

namespace NSA.Tests;

public sealed class BulkNotificationJobServiceTests
{
    [Fact]
    public async Task Queue_rejects_a_request_without_notifications()
    {
        await using var harness = CreateHarness();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            harness.Service.QueueAsync(
                new CreateBulkNotificationsRequest(Array.Empty<BulkNotificationItemRequest>()),
                "correlation",
                CancellationToken.None));

        Assert.Equal("At least one notification is required.", exception.Message);
        Assert.Empty(harness.Publisher.Messages);
    }

    [Fact]
    public async Task Queue_rejects_a_null_notification_collection()
    {
        await using var harness = CreateHarness();

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            harness.Service.QueueAsync(
                new CreateBulkNotificationsRequest(null!),
                "correlation",
                CancellationToken.None));
    }

    [Fact]
    public async Task Queue_persists_job_and_items_then_publishes_a_small_versioned_reference()
    {
        await using var harness = CreateHarness();
        var request = new CreateBulkNotificationsRequest(new[]
        {
            CreateItem(" first@example.com ", " First "),
            CreateItem("second@example.com", "Second")
        });

        var queued = await harness.Service.QueueAsync(request, "trace-123", CancellationToken.None);

        Assert.Equal(BulkNotificationJobStatuses.Queued, queued.Status);
        Assert.Equal("trace-123", queued.CorrelationId);
        Assert.Equal(2, queued.TotalCount);
        var persisted = await harness.Context.BulkNotificationJobs
            .AsNoTracking()
            .Include(job => job.Items)
            .SingleAsync(job => job.Id == queued.JobId);
        Assert.Equal(BulkNotificationRequestedV1.CurrentSchemaVersion, persisted.MessageSchemaVersion);
        Assert.Equal(new[] { 0, 1 }, persisted.Items.OrderBy(item => item.Sequence).Select(item => item.Sequence));
        Assert.Equal("first@example.com", persisted.Items.Single(item => item.Sequence == 0).RecipientEmail);
        Assert.Equal("First", persisted.Items.Single(item => item.Sequence == 0).Subject);

        var command = Assert.Single(harness.Publisher.Messages);
        Assert.Equal(BulkNotificationRequestedV1.CurrentSchemaVersion, command.SchemaVersion);
        Assert.Equal(queued.JobId, command.JobId);
        Assert.Equal("trace-123", command.CorrelationId);
        Assert.NotEqual(Guid.Empty, command.MessageId);
    }

    [Fact]
    public async Task Persisted_status_is_queryable_from_a_new_service_scope()
    {
        await using var harness = CreateHarness();
        var queued = await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(new[] { CreateItem("persisted@example.com", "Persisted") }),
            "cross-process",
            CancellationToken.None);

        await using var secondContext = new NotificationDbContext(harness.DatabaseOptions);
        var secondService = CreateService(
            secondContext,
            harness.Publisher,
            maxTrackedJobs: 10,
            maxBatchSize: 100,
            timeProvider: harness.TimeProvider);

        var status = await secondService.GetStatusAsync(queued.JobId, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(BulkNotificationJobStatuses.Queued, status.Status);
        Assert.Equal("cross-process", status.CorrelationId);
    }

    [Fact]
    public async Task Queue_enforces_the_configured_maximum_batch_size()
    {
        await using var harness = CreateHarness(maxBatchSize: 2);
        var request = new CreateBulkNotificationsRequest(new[]
        {
            CreateItem("one@example.com", "One"),
            CreateItem("two@example.com", "Two"),
            CreateItem("three@example.com", "Three")
        });

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            harness.Service.QueueAsync(request, "correlation", CancellationToken.None));

        Assert.Contains("more than 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queue_rejects_new_work_when_the_active_job_limit_is_reached()
    {
        await using var harness = CreateHarness(maxTrackedJobs: 1);
        var first = await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(new[] { CreateItem("first@example.com", "First") }),
            "first",
            CancellationToken.None);

        await Assert.ThrowsAsync<BulkNotificationCapacityException>(() =>
            harness.Service.QueueAsync(
                new CreateBulkNotificationsRequest(new[] { CreateItem("second@example.com", "Second") }),
                "second",
                CancellationToken.None));
        Assert.NotNull(await harness.Service.GetStatusAsync(first.JobId, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_failure_is_persisted_and_reported_as_service_unavailable()
    {
        var publisher = new RecordingPublisher(new InvalidOperationException("broker unavailable"));
        await using var harness = CreateHarness(publisher: publisher);

        var exception = await Assert.ThrowsAsync<BulkNotificationPublishException>(() =>
            harness.Service.QueueAsync(
                new CreateBulkNotificationsRequest(new[] { CreateItem("failure@example.com", "Failure") }),
                "publish-failure",
                CancellationToken.None));

        var failed = await harness.Context.BulkNotificationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(failed.Id, exception.Job.JobId);
        Assert.Equal("publish-failure", exception.Job.CorrelationId);
        Assert.Equal(BulkNotificationJobStatuses.PublishFailed, failed.Status);
        Assert.NotNull(failed.CompletedAtUtc);
        Assert.Equal("publish-failure", failed.CorrelationId);
        Assert.DoesNotContain("broker unavailable", failed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_publish_failure_does_not_overwrite_concurrent_worker_completion()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseOptions = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"ambiguous-publish-{Guid.NewGuid():N}", databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        await using var apiContext = new NotificationDbContext(databaseOptions);
        var publisher = new CallbackPublisher(async (message, cancellationToken) =>
        {
            await using var workerContext = new NotificationDbContext(databaseOptions);
            var concurrentlyProcessed = await workerContext.BulkNotificationJobs
                .SingleAsync(job => job.Id == message.JobId, cancellationToken);
            concurrentlyProcessed.Status = BulkNotificationJobStatuses.Completed;
            concurrentlyProcessed.ProcessedCount = concurrentlyProcessed.TotalCount;
            concurrentlyProcessed.SucceededCount = concurrentlyProcessed.TotalCount;
            concurrentlyProcessed.StartedAtUtc = DateTimeOffset.UtcNow;
            concurrentlyProcessed.CompletedAtUtc = DateTimeOffset.UtcNow;
            await workerContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("confirm connection lost after broker acceptance");
        });
        var service = CreateService(
            apiContext,
            publisher,
            maxTrackedJobs: 10,
            maxBatchSize: 100);

        await Assert.ThrowsAsync<BulkNotificationPublishException>(() =>
            service.QueueAsync(
                new CreateBulkNotificationsRequest(new[] { CreateItem("ambiguous@example.com", "Ambiguous") }),
                "ambiguous-confirm",
                CancellationToken.None));

        apiContext.ChangeTracker.Clear();
        var persisted = await apiContext.BulkNotificationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(BulkNotificationJobStatuses.Completed, persisted.Status);
        Assert.Equal(1, persisted.ProcessedCount);
        Assert.Equal(1, persisted.SucceededCount);
    }

    [Fact]
    public async Task Completed_jobs_are_removed_after_the_retention_period()
    {
        var now = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        await using var harness = CreateHarness(
            maxTrackedJobs: 1,
            completedRetentionMinutes: 1,
            timeProvider: timeProvider);
        harness.Context.BulkNotificationJobs.Add(new BulkNotificationJob
        {
            Id = Guid.NewGuid(),
            Status = BulkNotificationJobStatuses.Completed,
            MessageSchemaVersion = 1,
            CorrelationId = "expired",
            TotalCount = 1,
            ProcessedCount = 1,
            SucceededCount = 1,
            QueuedAtUtc = now.AddMinutes(-10),
            CompletedAtUtc = now.AddMinutes(-2)
        });
        await harness.Context.SaveChangesAsync();

        var replacement = await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(new[] { CreateItem("replacement@example.com", "Replacement") }),
            "replacement",
            CancellationToken.None);

        Assert.Single(await harness.Context.BulkNotificationJobs.AsNoTracking().ToListAsync());
        Assert.NotNull(await harness.Service.GetStatusAsync(replacement.JobId, CancellationToken.None));
    }

    [Fact]
    public async Task GetStatus_returns_null_for_an_unknown_job()
    {
        await using var harness = CreateHarness();

        Assert.Null(await harness.Service.GetStatusAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static BulkNotificationItemRequest CreateItem(string recipientEmail, string subject) =>
        new(recipientEmail, NotificationChannel.InApp, subject, "Body", null);

    private static TestHarness CreateHarness(
        int maxTrackedJobs = 10,
        int maxBatchSize = 100,
        int completedRetentionMinutes = 60,
        RecordingPublisher? publisher = null,
        TimeProvider? timeProvider = null)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseOptions = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"bulk-jobs-{Guid.NewGuid():N}", databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var context = new NotificationDbContext(databaseOptions);
        var actualPublisher = publisher ?? new RecordingPublisher();
        var actualTimeProvider = timeProvider ?? TimeProvider.System;
        var service = CreateService(
            context,
            actualPublisher,
            maxTrackedJobs,
            maxBatchSize,
            completedRetentionMinutes,
            actualTimeProvider);
        return new TestHarness(context, databaseOptions, service, actualPublisher, actualTimeProvider);
    }

    private static BulkNotificationJobService CreateService(
        NotificationDbContext context,
        IBulkNotificationCommandPublisher publisher,
        int maxTrackedJobs,
        int maxBatchSize,
        int completedRetentionMinutes = 60,
        TimeProvider? timeProvider = null) =>
        new(
            new BulkNotificationJobRepository(context),
            publisher,
            new BulkNotificationSettings(
                maxTrackedJobs,
                maxBatchSize,
                completedRetentionMinutes),
            timeProvider ?? TimeProvider.System,
            NullLogger<BulkNotificationJobService>.Instance);

    private sealed class RecordingPublisher(Exception? exception = null) : IBulkNotificationCommandPublisher
    {
        public List<BulkNotificationRequestedV1> Messages { get; } = new();

        public Task PublishAsync(BulkNotificationRequestedV1 message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }

    private sealed class CallbackPublisher(
        Func<BulkNotificationRequestedV1, CancellationToken, Task> callback)
        : IBulkNotificationCommandPublisher
    {
        public Task PublishAsync(
            BulkNotificationRequestedV1 message,
            CancellationToken cancellationToken) => callback(message, cancellationToken);
    }

    private sealed record TestHarness(
        NotificationDbContext Context,
        DbContextOptions<NotificationDbContext> DatabaseOptions,
        BulkNotificationJobService Service,
        RecordingPublisher Publisher,
        TimeProvider TimeProvider) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
