using Microsoft.Extensions.Options;
using NSA.Application.Exceptions;
using NSA.Application.Contracts;
using NSA.Domain.Enums;
using NSA.Service;

namespace NSA.Tests;

public sealed class BulkNotificationJobServiceTests
{
    [Fact]
    public void Queue_rejects_a_request_without_notifications()
    {
        var service = new BulkNotificationJobService();
        var request = new CreateBulkNotificationsRequest(Array.Empty<BulkNotificationItemRequest>());

        var exception = Assert.Throws<RequestValidationException>(() => service.Queue(request));

        Assert.Equal("At least one notification is required.", exception.Message);
    }

    [Fact]
    public void Queue_rejects_a_null_notification_collection()
    {
        var service = new BulkNotificationJobService();
        var request = new CreateBulkNotificationsRequest(null!);

        Assert.Throws<RequestValidationException>(() => service.Queue(request));
    }

    [Fact]
    public void Queue_returns_a_stable_queued_snapshot_and_copies_the_input()
    {
        var notifications = new List<BulkNotificationItemRequest>
        {
            CreateItem("first@example.com", "First")
        };
        var service = new BulkNotificationJobService();

        var queued = service.Queue(new CreateBulkNotificationsRequest(notifications));
        notifications.Add(CreateItem("second@example.com", "Second"));
        var status = service.GetStatus(queued.JobId);

        Assert.NotEqual(Guid.Empty, queued.JobId);
        Assert.Equal("Queued", queued.Status);
        Assert.Equal(1, queued.TotalCount);
        Assert.Equal(0, queued.ProcessedCount);
        Assert.Equal(queued, status);
    }

    [Fact]
    public void GetStatus_returns_null_for_an_unknown_job()
    {
        var service = new BulkNotificationJobService();

        Assert.Null(service.GetStatus(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReadAllAsync_yields_the_job_that_was_queued()
    {
        var service = new BulkNotificationJobService();
        var queued = service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("reader@example.com", "Read me") }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = service.ReadAllAsync(timeout.Token).GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(queued.JobId, reader.Current.Id);
        Assert.Equal("reader@example.com", reader.Current.Notifications.Single().RecipientEmail);
    }

    [Fact]
    public void Queue_enforces_the_configured_maximum_batch_size()
    {
        var service = CreateService(queueCapacity: 10, maxTrackedJobs: 10, maxBatchSize: 2);
        var request = new CreateBulkNotificationsRequest(new[]
        {
            CreateItem("one@example.com", "One"),
            CreateItem("two@example.com", "Two"),
            CreateItem("three@example.com", "Three")
        });

        var exception = Assert.Throws<RequestValidationException>(() => service.Queue(request));

        Assert.Contains("more than 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Queue_rejects_new_work_when_the_tracked_job_limit_is_reached()
    {
        var service = CreateService(queueCapacity: 2, maxTrackedJobs: 1);
        var first = service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("first@example.com", "First") }));

        Assert.Throws<ServiceUnavailableException>(() => service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("second@example.com", "Second") })));
        Assert.NotNull(service.GetStatus(first.JobId));
    }

    [Fact]
    public async Task Parallel_queue_admission_never_exceeds_the_tracked_job_limit()
    {
        const int maxTrackedJobs = 8;
        const int attemptCount = 128;
        var service = CreateService(queueCapacity: attemptCount, maxTrackedJobs: maxTrackedJobs);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, attemptCount)
            .Select(async index =>
            {
                await start.Task;
                try
                {
                    return service.Queue(new CreateBulkNotificationsRequest(
                        new[] { CreateItem($"parallel-{index}@example.com", $"Parallel {index}") }));
                }
                catch (ServiceUnavailableException)
                {
                    return null;
                }
            })
            .ToArray();

        start.SetResult();
        var results = await Task.WhenAll(attempts);
        var accepted = results.Where(result => result is not null).Cast<BulkNotificationJobDto>().ToArray();

        Assert.Equal(maxTrackedJobs, accepted.Length);
        Assert.Equal(attemptCount - maxTrackedJobs, results.Count(result => result is null));
        Assert.All(accepted, job => Assert.NotNull(service.GetStatus(job.JobId)));
    }

    [Fact]
    public async Task Full_queue_rejects_work_without_leaving_an_orphaned_tracked_job()
    {
        var service = CreateService(queueCapacity: 1, maxTrackedJobs: 2);
        service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("first@example.com", "First") }));

        Assert.Throws<ServiceUnavailableException>(() => service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("rejected@example.com", "Rejected") })));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using (var reader = service.ReadAllAsync(timeout.Token).GetAsyncEnumerator())
        {
            Assert.True(await reader.MoveNextAsync());
        }

        var acceptedAfterCapacityWasReleased = service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("third@example.com", "Third") }));
        Assert.NotNull(service.GetStatus(acceptedAfterCapacityWasReleased.JobId));
    }

    [Fact]
    public void Job_records_successful_and_failed_items_before_completing_with_errors()
    {
        var queuedAt = new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);
        var job = new BulkNotificationJob(
            Guid.NewGuid(),
            new[]
            {
                CreateItem("success@example.com", "Success"),
                CreateItem("failure@example.com", "Failure"),
                CreateItem("later-failure@example.com", "Later failure")
            },
            queuedAt);

        job.Start();
        var processing = job.ToDto();
        job.RecordSuccess();
        job.RecordFailure(new InvalidOperationException("provider unavailable"));
        job.RecordFailure(new InvalidOperationException("later error"));
        job.Complete();
        var completed = job.ToDto();

        Assert.Equal("Processing", processing.Status);
        Assert.NotNull(processing.StartedAtUtc);
        Assert.Equal("CompletedWithErrors", completed.Status);
        Assert.Equal(3, completed.ProcessedCount);
        Assert.Equal(1, completed.SucceededCount);
        Assert.Equal(2, completed.FailedCount);
        Assert.Equal("One or more notifications could not be processed.", completed.Error);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Equal(queuedAt, completed.QueuedAtUtc);
        Assert.Equal(3, completed.TotalCount);
        Assert.Empty(job.Notifications);
    }

    [Fact]
    public async Task Terminal_job_releases_payload_but_retains_its_status_snapshot()
    {
        var service = CreateService(queueCapacity: 2, maxTrackedJobs: 1);
        var queued = service.Queue(new CreateBulkNotificationsRequest(new[]
        {
            CreateItem("first@example.com", "First"),
            CreateItem("second@example.com", "Second")
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = service.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        var job = reader.Current;

        Assert.Equal(2, job.Notifications.Count);
        job.Start();
        job.RecordSuccess();
        job.RecordFailure(new InvalidOperationException("provider unavailable"));
        job.Complete();

        Assert.Empty(job.Notifications);
        var retained = Assert.IsType<BulkNotificationJobDto>(service.GetStatus(queued.JobId));
        Assert.Equal("CompletedWithErrors", retained.Status);
        Assert.Equal(2, retained.TotalCount);
        Assert.Equal(2, retained.ProcessedCount);
        Assert.Equal(1, retained.SucceededCount);
        Assert.Equal(1, retained.FailedCount);
        Assert.Throws<ServiceUnavailableException>(() => service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("replacement@example.com", "Replacement") })));
    }

    [Fact]
    public void Cancelled_job_releases_payload_and_retains_the_original_total()
    {
        var job = new BulkNotificationJob(
            Guid.NewGuid(),
            new[]
            {
                CreateItem("first@example.com", "First"),
                CreateItem("second@example.com", "Second")
            },
            DateTimeOffset.UtcNow);

        job.Start();
        job.RecordSuccess();
        job.Cancel();
        var cancelled = job.ToDto();

        Assert.Empty(job.Notifications);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(2, cancelled.TotalCount);
        Assert.Equal(1, cancelled.ProcessedCount);
        Assert.Equal(1, cancelled.SucceededCount);
    }

    [Fact]
    public void Only_terminal_jobs_older_than_the_retention_cutoff_are_expired()
    {
        var job = new BulkNotificationJob(
            Guid.NewGuid(),
            new[] { CreateItem("retention@example.com", "Retention") },
            DateTimeOffset.UtcNow);

        Assert.False(job.CompletedBefore(DateTimeOffset.MaxValue));

        job.Start();
        job.RecordSuccess();
        job.Complete();
        var completedAt = Assert.IsType<DateTimeOffset>(job.ToDto().CompletedAtUtc);

        Assert.False(job.CompletedBefore(completedAt));
        Assert.True(job.CompletedBefore(completedAt.AddTicks(1)));
    }

    [Fact]
    public async Task Completed_jobs_are_removed_after_the_configured_retention_period()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero));
        var service = CreateService(queueCapacity: 1, maxTrackedJobs: 1, timeProvider: timeProvider);
        var queued = service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("retention@example.com", "Retention") }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = service.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());

        reader.Current.Start();
        reader.Current.RecordSuccess();
        reader.Current.Complete();
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(service.GetStatus(queued.JobId));
        var replacement = service.Queue(new CreateBulkNotificationsRequest(
            new[] { CreateItem("replacement@example.com", "Replacement") }));
        Assert.NotNull(service.GetStatus(replacement.JobId));
    }

    private static BulkNotificationItemRequest CreateItem(string recipientEmail, string subject) =>
        new(recipientEmail, NotificationChannel.Email, subject, "Body", null);

    private static BulkNotificationJobService CreateService(
        int queueCapacity,
        int maxTrackedJobs,
        int maxBatchSize = 100,
        TimeProvider? timeProvider = null) =>
        new(Options.Create(new BulkNotificationOptions
        {
            QueueCapacity = queueCapacity,
            MaxTrackedJobs = maxTrackedJobs,
            MaxBatchSize = maxBatchSize,
            CompletedJobRetentionMinutes = 1
        }), timeProvider ?? TimeProvider.System);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void Advance(TimeSpan duration)
        {
            currentUtcNow = currentUtcNow.Add(duration);
        }
    }
}
