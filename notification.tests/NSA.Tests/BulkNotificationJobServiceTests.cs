using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Infrastructure.Configuration;
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
    public async Task Queue_persists_job_items_and_a_small_versioned_outbox_reference()
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

        var outbox = Assert.Single(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        var command = JsonSerializer.Deserialize<BulkNotificationRequestedV1>(
            outbox.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(command);
        Assert.Equal(BulkNotificationRequestedV1.CurrentSchemaVersion, command.SchemaVersion);
        Assert.Equal(queued.JobId, command.JobId);
        Assert.Equal("trace-123", command.CorrelationId);
        Assert.NotEqual(Guid.Empty, command.MessageId);
        Assert.Equal(outbox.Id, command.MessageId);
        Assert.Null(outbox.PublishedAtUtc);
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
    public async Task Queue_does_not_contact_the_broker_and_leaves_a_pending_outbox_message()
    {
        await using var harness = CreateHarness();

        var result = await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(new[] { CreateItem("durable@example.com", "Durable") }),
            "broker-independent",
            CancellationToken.None);

        Assert.Equal(BulkNotificationJobStatuses.Queued, result.Status);
        var outbox = await harness.Context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(outbox.PublishedAtUtc);
        Assert.Equal(0, outbox.AttemptCount);
        Assert.Equal(result.JobId.ToString("D"), outbox.AggregateId);
    }

    [Fact]
    public async Task Idempotency_replays_the_original_job_and_rejects_a_different_payload()
    {
        await using var harness = CreateHarness();
        var idempotency = new IdempotencyContext("bulk-v2", "actor-1", "request-123");
        var request = new CreateBulkNotificationsRequest(
            new[] { CreateItem("same@example.com", "Same") });

        var first = await harness.Service.QueueAsync(
            request, "first-correlation", idempotency, CancellationToken.None);
        var replay = await harness.Service.QueueAsync(
            request, "retry-correlation", idempotency, CancellationToken.None);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Value, replay.Value);
        Assert.Single(await harness.Context.BulkNotificationJobs.AsNoTracking().ToListAsync());
        Assert.Single(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        var receipt = await harness.Context.IdempotencyRecords.AsNoTracking().SingleAsync();
        Assert.NotEqual(idempotency.Key, receipt.KeyHash);
        Assert.Equal(64, receipt.KeyHash.Length);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            harness.Service.QueueAsync(
                new CreateBulkNotificationsRequest(
                    new[] { CreateItem("different@example.com", "Different") }),
                "conflict",
                idempotency,
                CancellationToken.None));
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
        var expiredJob = new BulkNotificationJob
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
        };
        harness.Context.BulkNotificationJobs.Add(expiredJob);
        harness.Context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = nameof(BulkNotificationJob),
            AggregateId = expiredJob.Id.ToString("D"),
            MessageType = BulkNotificationRequestedV1.MessageType,
            SchemaVersion = 1,
            Payload = "{}",
            Exchange = "test.commands",
            RoutingKey = "bulk.requested",
            CorrelationId = expiredJob.CorrelationId,
            OccurredAtUtc = expiredJob.QueuedAtUtc,
            NextAttemptAtUtc = expiredJob.QueuedAtUtc,
            PublishedAtUtc = expiredJob.CompletedAtUtc
        });
        await harness.Context.SaveChangesAsync();

        var replacement = await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(new[] { CreateItem("replacement@example.com", "Replacement") }),
            "replacement",
            CancellationToken.None);

        Assert.Single(await harness.Context.BulkNotificationJobs.AsNoTracking().ToListAsync());
        var retainedOutbox = await harness.Context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(replacement.JobId.ToString("D"), retainedOutbox.AggregateId);
        Assert.NotNull(await harness.Service.GetStatusAsync(replacement.JobId, CancellationToken.None));
    }

    [Fact]
    public async Task Completed_jobs_referenced_by_visible_notifications_survive_retention_cleanup()
    {
        var now = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);
        await using var harness = CreateHarness(
            maxTrackedJobs: 1,
            completedRetentionMinutes: 1,
            timeProvider: new ManualTimeProvider(now));
        var retainedJob = new BulkNotificationJob
        {
            Id = Guid.NewGuid(),
            Status = BulkNotificationJobStatuses.Completed,
            MessageSchemaVersion = 1,
            CorrelationId = "visible-notification",
            TotalCount = 1,
            ProcessedCount = 1,
            SucceededCount = 1,
            QueuedAtUtc = now.AddMinutes(-10),
            CompletedAtUtc = now.AddMinutes(-2),
            Items =
            [
                new BulkNotificationJobItem
                {
                    Sequence = 0,
                    RecipientEmail = "visible@example.test",
                    Channel = NotificationChannel.InApp,
                    Subject = "Visible",
                    Body = "Visible body",
                    Status = BulkNotificationItemStatuses.Succeeded
                }
            ]
        };
        harness.Context.BulkNotificationJobs.Add(retainedJob);
        await harness.Context.SaveChangesAsync();
        var retainedItem = Assert.Single(retainedJob.Items);
        harness.Context.Notifications.Add(new Notification
        {
            RecipientEmail = retainedItem.RecipientEmail,
            Channel = retainedItem.Channel,
            Subject = retainedItem.Subject,
            Body = retainedItem.Body,
            IsRead = false,
            CreatedAtUtc = now.AddMinutes(-2),
            BulkNotificationJobId = retainedJob.Id,
            BulkNotificationJobItemId = retainedItem.Id
        });
        await harness.Context.SaveChangesAsync();

        await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(
                new[] { CreateItem("replacement@example.com", "Replacement") }),
            "replacement",
            CancellationToken.None);

        Assert.True(await harness.Context.BulkNotificationJobs.AnyAsync(
            job => job.Id == retainedJob.Id));
        Assert.Equal(2, await harness.Context.BulkNotificationJobs.CountAsync());
    }

    [Fact]
    public async Task Dead_lettered_jobs_and_their_outbox_history_survive_retention_cleanup()
    {
        var now = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);
        await using var harness = CreateHarness(
            completedRetentionMinutes: 1,
            timeProvider: new ManualTimeProvider(now));
        var deadLettered = new BulkNotificationJob
        {
            Id = Guid.NewGuid(),
            Status = BulkNotificationJobStatuses.DeadLettered,
            MessageSchemaVersion = 1,
            CorrelationId = "awaiting-dlq-recovery",
            TotalCount = 1,
            QueuedAtUtc = now.AddHours(-2),
            CompletedAtUtc = now.AddHours(-1)
        };
        harness.Context.BulkNotificationJobs.Add(deadLettered);
        harness.Context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = nameof(BulkNotificationJob),
            AggregateId = deadLettered.Id.ToString("D"),
            MessageType = BulkNotificationRequestedV1.MessageType,
            SchemaVersion = 1,
            Payload = "{}",
            Exchange = "test.commands",
            RoutingKey = "bulk.requested",
            CorrelationId = deadLettered.CorrelationId,
            OccurredAtUtc = deadLettered.QueuedAtUtc,
            NextAttemptAtUtc = deadLettered.QueuedAtUtc,
            PublishedAtUtc = deadLettered.QueuedAtUtc
        });
        await harness.Context.SaveChangesAsync();

        await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(
                new[] { CreateItem("replacement@example.test", "Replacement") }),
            "replacement",
            CancellationToken.None);

        Assert.True(await harness.Context.BulkNotificationJobs.AnyAsync(
            job => job.Id == deadLettered.Id));
        Assert.True(await harness.Context.OutboxMessages.AnyAsync(
            message => message.AggregateId == deadLettered.Id.ToString("D")));
    }

    [Fact]
    public async Task Expired_partial_success_graph_is_removed_with_notifications_and_outbox()
    {
        var now = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);
        await using var harness = CreateHarness(
            completedRetentionMinutes: 1,
            timeProvider: new ManualTimeProvider(now));
        var partial = new BulkNotificationJob
        {
            Id = Guid.NewGuid(),
            Status = BulkNotificationJobStatuses.CompletedWithErrors,
            MessageSchemaVersion = 1,
            CorrelationId = "partial-success",
            TotalCount = 1,
            ProcessedCount = 1,
            SucceededCount = 1,
            QueuedAtUtc = now.AddMinutes(-10),
            CompletedAtUtc = now.AddMinutes(-2),
            Items =
            [
                new BulkNotificationJobItem
                {
                    Sequence = 0,
                    RecipientEmail = "partial@example.test",
                    Channel = NotificationChannel.InApp,
                    Subject = "Partial",
                    Body = "Partial body",
                    Status = BulkNotificationItemStatuses.Succeeded
                }
            ]
        };
        harness.Context.BulkNotificationJobs.Add(partial);
        await harness.Context.SaveChangesAsync();
        var item = Assert.Single(partial.Items);
        harness.Context.Notifications.Add(new Notification
        {
            RecipientEmail = item.RecipientEmail,
            Channel = item.Channel,
            Subject = item.Subject,
            Body = item.Body,
            IsRead = false,
            CreatedAtUtc = partial.CompletedAtUtc.Value,
            BulkNotificationJobId = partial.Id,
            BulkNotificationJobItemId = item.Id
        });
        harness.Context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = nameof(BulkNotificationJob),
            AggregateId = partial.Id.ToString("D"),
            MessageType = BulkNotificationRequestedV1.MessageType,
            SchemaVersion = 1,
            Payload = "{}",
            Exchange = "test.commands",
            RoutingKey = "bulk.requested",
            CorrelationId = partial.CorrelationId,
            OccurredAtUtc = partial.QueuedAtUtc,
            NextAttemptAtUtc = partial.QueuedAtUtc,
            PublishedAtUtc = partial.CompletedAtUtc
        });
        await harness.Context.SaveChangesAsync();

        await harness.Service.QueueAsync(
            new CreateBulkNotificationsRequest(
                new[] { CreateItem("replacement@example.test", "Replacement") }),
            "replacement",
            CancellationToken.None);

        Assert.False(await harness.Context.BulkNotificationJobs.AnyAsync(
            job => job.Id == partial.Id));
        Assert.False(await harness.Context.Notifications.AnyAsync(
            notification => notification.BulkNotificationJobId == partial.Id));
        Assert.False(await harness.Context.OutboxMessages.AnyAsync(
            message => message.AggregateId == partial.Id.ToString("D")));
    }

    [Fact]
    public async Task Replay_check_removes_a_receipt_that_expired_after_the_initial_purge_window()
    {
        var now = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);
        await using var harness = CreateHarness(timeProvider: new ManualTimeProvider(now));
        var context = new IdempotencyContext("bulk-v2", "actor-1", "expired-key");
        var originalStore = new IdempotencyStore(
            harness.Context,
            new DurabilityOptions { IdempotencyRetentionHours = 1 },
            new ManualTimeProvider(now.AddHours(-2)));
        originalStore.Add(
            context,
            "request-hash",
            StatusCodes.Status202Accepted,
            "original-response",
            "test",
            "resource-1");
        await harness.Context.SaveChangesAsync();
        var currentStore = new IdempotencyStore(
            harness.Context,
            new DurabilityOptions { IdempotencyRetentionHours = 1 },
            new ManualTimeProvider(now));

        var replay = await currentStore.TryReplayAsync<string>(
            context,
            "different-request-hash",
            CancellationToken.None);

        Assert.Null(replay);
        Assert.Empty(await harness.Context.IdempotencyRecords.ToListAsync());
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
        TimeProvider? timeProvider = null)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseOptions = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"bulk-jobs-{Guid.NewGuid():N}", databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var context = new NotificationDbContext(databaseOptions);
        var actualTimeProvider = timeProvider ?? TimeProvider.System;
        var service = CreateService(
            context,
            maxTrackedJobs,
            maxBatchSize,
            completedRetentionMinutes,
            actualTimeProvider);
        return new TestHarness(context, databaseOptions, service, actualTimeProvider);
    }

    private static BulkNotificationJobService CreateService(
        NotificationDbContext context,
        int maxTrackedJobs,
        int maxBatchSize,
        int completedRetentionMinutes = 60,
        TimeProvider? timeProvider = null) =>
        new(
            new BulkNotificationJobRepository(context),
            context,
            new IdempotencyStore(
                context,
                new DurabilityOptions(),
                timeProvider ?? TimeProvider.System),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RabbitMq:CommandExchange"] = "test.commands",
                    ["RabbitMq:CommandRoutingKey"] = "bulk.requested"
                })
                .Build(),
            new BulkNotificationSettings(
                maxTrackedJobs,
                maxBatchSize,
                completedRetentionMinutes),
            timeProvider ?? TimeProvider.System);

    private sealed record TestHarness(
        NotificationDbContext Context,
        DbContextOptions<NotificationDbContext> DatabaseOptions,
        BulkNotificationJobService Service,
        TimeProvider TimeProvider) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
