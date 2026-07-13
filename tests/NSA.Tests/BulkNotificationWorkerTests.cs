using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Enums;
using NSA.Infrastructure.BackgroundServices;
using NSA.Service;

namespace NSA.Tests;

public sealed class BulkNotificationWorkerTests
{
    [Fact]
    public async Task Worker_exposes_processing_then_completed_for_a_successful_job()
    {
        var notificationService = new ControlledNotificationService(pauseFirstCall: true);
        await using var provider = BuildProvider(notificationService);
        var jobs = new BulkNotificationJobService();
        var worker = CreateWorker(jobs, provider.GetRequiredService<IServiceScopeFactory>());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var queued = jobs.Queue(CreateRequest("success"));
            await notificationService.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(2));

            var processing = jobs.GetStatus(queued.JobId);
            Assert.NotNull(processing);
            Assert.Equal("Processing", processing.Status);
            Assert.NotNull(processing.StartedAtUtc);
            Assert.Null(processing.CompletedAtUtc);

            notificationService.ReleaseFirstCall();
            var completed = await WaitForTerminalStatusAsync(jobs, queued.JobId);

            Assert.Equal("Completed", completed.Status);
            Assert.Equal(1, completed.ProcessedCount);
            Assert.Equal(1, completed.SucceededCount);
            Assert.Equal(0, completed.FailedCount);
            Assert.Null(completed.Error);
            Assert.NotNull(completed.CompletedAtUtc);
        }
        finally
        {
            notificationService.ReleaseFirstCall();
            await StopWorkerAsync(worker);
        }
    }

    [Fact]
    public async Task Worker_counts_an_item_failure_and_continues_the_remaining_job()
    {
        var notificationService = new ControlledNotificationService(failingSubject: "fail");
        await using var provider = BuildProvider(notificationService);
        var jobs = new BulkNotificationJobService();
        var worker = CreateWorker(jobs, provider.GetRequiredService<IServiceScopeFactory>());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var queued = jobs.Queue(CreateRequest("succeed", "fail", "also-succeed"));
            var completed = await WaitForTerminalStatusAsync(jobs, queued.JobId);

            Assert.Equal("CompletedWithErrors", completed.Status);
            Assert.Equal(3, completed.ProcessedCount);
            Assert.Equal(2, completed.SucceededCount);
            Assert.Equal(1, completed.FailedCount);
            Assert.Equal("One or more notifications could not be processed.", completed.Error);
            Assert.Equal(new[] { "succeed", "fail", "also-succeed" }, notificationService.Subjects);
        }
        finally
        {
            await StopWorkerAsync(worker);
        }
    }

    [Fact]
    public async Task Worker_marks_an_inflight_job_cancelled_during_graceful_shutdown()
    {
        var notificationService = new ControlledNotificationService(pauseFirstCall: true);
        await using var provider = BuildProvider(notificationService);
        var jobs = new BulkNotificationJobService();
        var worker = CreateWorker(jobs, provider.GetRequiredService<IServiceScopeFactory>());

        await worker.StartAsync(CancellationToken.None);
        var queued = jobs.Queue(CreateRequest("inflight"));
        await notificationService.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(2));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StopAsync(timeout.Token);
        var cancelled = jobs.GetStatus(queued.JobId);
        worker.Dispose();

        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(0, cancelled.ProcessedCount);
        Assert.NotNull(cancelled.CompletedAtUtc);
    }

    private static ServiceProvider BuildProvider(ControlledNotificationService notificationService) =>
        new ServiceCollection()
            .AddScoped<INotificationService>(_ => notificationService)
            .AddScoped<INotificationDispatcher>(_ => new ControlledNotificationDispatcher(notificationService))
            .BuildServiceProvider();

    private static BulkNotificationWorker CreateWorker(BulkNotificationJobService jobs, IServiceScopeFactory scopeFactory) =>
        new(jobs, scopeFactory, NullLogger<BulkNotificationWorker>.Instance);

    private static CreateBulkNotificationsRequest CreateRequest(params string[] subjects) =>
        new(subjects.Select(subject => new BulkNotificationItemRequest(
            $"{subject}@example.com",
            NotificationChannel.Email,
            subject,
            "Body",
            null)).ToArray());

    private static async Task<BulkNotificationJobDto> WaitForTerminalStatusAsync(
        BulkNotificationJobService jobs,
        Guid jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!timeout.IsCancellationRequested)
        {
            var status = jobs.GetStatus(jobId);
            if (status is not null && status.Status is "Completed" or "CompletedWithErrors")
            {
                return status;
            }

            await Task.Delay(10, timeout.Token);
        }

        throw new TimeoutException($"Job {jobId} did not reach a terminal state.");
    }

    private static async Task StopWorkerAsync(BulkNotificationWorker worker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StopAsync(timeout.Token);
        worker.Dispose();
    }

    private sealed class ControlledNotificationService(
        bool pauseFirstCall = false,
        string? failingSubject = null) : INotificationService
    {
        private readonly TaskCompletionSource firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource firstCallRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> subjects = new();
        private int nextId;

        public Task FirstCallStarted => firstCallStarted.Task;

        public IReadOnlyList<string> Subjects
        {
            get
            {
                lock (subjects)
                {
                    return subjects.ToArray();
                }
            }
        }

        public void ReleaseFirstCall() => firstCallRelease.TrySetResult();

        public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(
            string? recipientEmail,
            int? orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NotificationDto?> GetNotificationAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<NotificationDto> CreateNotificationAsync(
            CreateNotificationRequest request,
            CancellationToken cancellationToken)
        {
            lock (subjects)
            {
                subjects.Add(request.Subject);
            }

            if (pauseFirstCall && firstCallStarted.TrySetResult())
            {
                await firstCallRelease.Task.WaitAsync(cancellationToken);
            }

            if (request.Subject == failingSubject)
            {
                throw new InvalidOperationException($"Simulated failure for {request.Subject}.");
            }

            var now = DateTimeOffset.UtcNow;
            return new NotificationDto(
                Interlocked.Increment(ref nextId),
                request.RecipientEmail,
                request.Channel,
                request.Subject,
                request.Body,
                request.OrderId,
                false,
                now,
                null);
        }

        public Task<NotificationDto?> UpdateNotificationAsync(
            int id,
            UpdateNotificationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteNotificationAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledNotificationDispatcher(ControlledNotificationService notificationService)
        : INotificationDispatcher
    {
        public async Task<NSA.Domain.Entities.Notification> CreateEmailNotificationAsync(
            string recipientEmail,
            string subject,
            string body,
            int? orderId,
            CancellationToken cancellationToken)
        {
            await notificationService.CreateNotificationAsync(
                new CreateNotificationRequest(
                    recipientEmail,
                    NotificationChannel.Email,
                    subject,
                    body,
                    orderId),
                cancellationToken);

            return NSA.Domain.Entities.Notification.Create(
                recipientEmail,
                NotificationChannel.Email,
                subject,
                body,
                orderId,
                DateTimeOffset.UtcNow);
        }
    }
}
