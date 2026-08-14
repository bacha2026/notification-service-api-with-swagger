using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Service;

namespace NSA.Tests;

public sealed class BulkNotificationDeadLetterRecoveryTests
{
    [Fact]
    public async Task Recovered_dead_lettered_job_processes_admin_and_visitor_notifications()
    {
        await using var context = CreateContext();
        var job = CreateDeadLetteredJob();
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();

        var recovery = new BulkNotificationDeadLetterRecoveryService(
            new BulkNotificationJobRepository(context),
            TimeProvider.System,
            NullLogger<BulkNotificationDeadLetterRecoveryService>.Instance);

        var disposition = await recovery.PrepareForReplayAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationDeadLetterRecoveryResult.RecoveryPrepared, disposition);
        Assert.Equal(BulkNotificationJobStatuses.RecoveryPending, job.Status);
        Assert.Null(job.CompletedAtUtc);
        Assert.Equal("Requeued by the dead-letter recovery worker.", job.Error);

        var notificationService = new NotificationService(
            new NotificationRepository(context),
            TimeProvider.System);
        var processor = new BulkNotificationProcessor(
            new BulkNotificationJobRepository(context),
            notificationService,
            new ConfiguredBulkNotificationFailureInjector(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>())
                    .Build()),
            TimeProvider.System,
            NullLogger<BulkNotificationProcessor>.Instance);

        var processingDisposition = await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationProcessingResult.Completed, processingDisposition);
        Assert.Equal(BulkNotificationJobStatuses.Completed, job.Status);
        Assert.Equal(2, job.ProcessedCount);
        Assert.Equal(2, job.SucceededCount);
        Assert.Equal(0, job.FailedCount);
        Assert.Equal(
            new[] { "admin@example.test", "visitor@example.test" },
            context.Notifications
                .OrderBy(notification => notification.RecipientEmail)
                .Select(notification => notification.RecipientEmail)
                .ToArray());
    }

    [Theory]
    [InlineData(BulkNotificationJobStatuses.Completed)]
    [InlineData(BulkNotificationJobStatuses.CompletedWithErrors)]
    public async Task Completed_jobs_are_acknowledged_without_replay(string status)
    {
        await using var context = CreateContext();
        var job = CreateDeadLetteredJob();
        job.Status = status;
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();

        var recovery = new BulkNotificationDeadLetterRecoveryService(
            new BulkNotificationJobRepository(context),
            TimeProvider.System,
            NullLogger<BulkNotificationDeadLetterRecoveryService>.Instance);

        var disposition = await recovery.PrepareForReplayAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationDeadLetterRecoveryResult.NoRecoveryRequired, disposition);
        Assert.Equal(status, job.Status);
    }

    [Fact]
    public async Task Unknown_job_is_parked_instead_of_being_replayed()
    {
        await using var context = CreateContext();
        var recovery = new BulkNotificationDeadLetterRecoveryService(
            new BulkNotificationJobRepository(context),
            TimeProvider.System,
            NullLogger<BulkNotificationDeadLetterRecoveryService>.Instance);

        var disposition = await recovery.PrepareForReplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BulkNotificationDeadLetterRecoveryResult.JobNotFound, disposition);
    }

    private static NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"dead-letter-recovery-{Guid.NewGuid():N}")
            .Options);

    private static BulkNotificationJob CreateDeadLetteredJob()
    {
        var jobId = Guid.NewGuid();
        return new BulkNotificationJob
        {
            Id = jobId,
            Status = BulkNotificationJobStatuses.DeadLettered,
            MessageSchemaVersion = BulkNotificationRequestedV1.CurrentSchemaVersion,
            CorrelationId = $"correlation-{jobId:N}",
            TotalCount = 2,
            QueuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Error = "Processing failed after 3 attempts; the command was routed to the dead-letter queue.",
            Items =
            [
                new BulkNotificationJobItem
                {
                    JobId = jobId,
                    Sequence = 0,
                    RecipientEmail = "admin@example.test",
                    Channel = NotificationChannel.InApp,
                    Subject = "New order #42",
                    Body = "Order notification for the administrator.",
                    Status = BulkNotificationItemStatuses.Pending
                },
                new BulkNotificationJobItem
                {
                    JobId = jobId,
                    Sequence = 1,
                    RecipientEmail = "visitor@example.test",
                    Channel = NotificationChannel.InApp,
                    Subject = "Order #42 received",
                    Body = "Order notification for the visitor.",
                    Status = BulkNotificationItemStatuses.Pending
                }
            ]
        };
    }
}
