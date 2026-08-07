using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;

namespace NSA.Service;

public sealed class BulkNotificationJobNotFoundException(Guid jobId)
    : Exception($"Bulk notification job {jobId} does not exist.");

public enum BulkNotificationProcessDisposition
{
    Acknowledge,
    DeadLetter
}

public sealed class BulkNotificationProcessor(
    NotificationDbContext dbContext,
    INotificationService notificationService,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    TimeProvider timeProvider,
    ILogger<BulkNotificationProcessor> logger)
{
    public async Task<BulkNotificationProcessDisposition> ProcessAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        //loads the bulk notification job from the database, including its items, and throws an exception if not found
        var job = await dbContext.BulkNotificationJobs
            .Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken)
            ?? throw new BulkNotificationJobNotFoundException(jobId);

        if (job.Status == BulkNotificationJobStatuses.DeadLettered)
        {
            logger.LogInformation(
                "Bulk notification job {JobId} is already dead-lettered; rejecting a redelivered command to the DLQ.",
                job.Id);
            return BulkNotificationProcessDisposition.DeadLetter;
        }

        if (job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors)
        {
            logger.LogInformation(
                "Bulk notification job {JobId} is already terminal with status {Status}; acknowledging duplicate command.",
                job.Id,
                job.Status);
            return BulkNotificationProcessDisposition.Acknowledge;
        }

        // A publish failure can be ambiguous: RabbitMQ may have accepted a command
        // before the confirm connection was lost. If that command arrives, it is the
        // proof needed to recover the persisted job instead of discarding valid work.
        var recoveringAmbiguousPublish = job.Status == BulkNotificationJobStatuses.PublishFailed;

        var failureInjectionSubject = rabbitMqOptions.Value.FailureInjectionSubject;
        if (!string.IsNullOrWhiteSpace(failureInjectionSubject)
            && job.Items.Any(item =>
                item.Status == BulkNotificationItemStatuses.Pending
                && string.Equals(item.Subject, failureInjectionSubject, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The opt-in Week 3 poison-message failure was triggered.");
        }

        job.Status = BulkNotificationJobStatuses.Processing;
        job.StartedAtUtc ??= timeProvider.GetUtcNow();
        if (recoveringAmbiguousPublish)
        {
            job.CompletedAtUtc = null;
        }
        job.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var item in job.Items
                     .Where(candidate => candidate.Status == BulkNotificationItemStatuses.Pending)
                     .OrderBy(candidate => candidate.Sequence))
        {
            item.AttemptCount++;
            try
            {
                await notificationService.CreateNotificationAsync(
                    new CreateNotificationRequest(
                        item.RecipientEmail,
                        item.Channel,
                        item.Subject,
                        item.Body,
                        item.OrderId),
                    cancellationToken);

                item.Status = BulkNotificationItemStatuses.Succeeded;
                item.LastError = null;
                job.ProcessedCount++;
                job.SucceededCount++;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (
                exception is RequestValidationException or ArgumentException
                && !cancellationToken.IsCancellationRequested)
            {
                item.Status = BulkNotificationItemStatuses.Failed;
                item.LastError = $"{exception.GetType().Name}: validation failed";
                job.ProcessedCount++;
                job.FailedCount++;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning(
                    exception,
                    "Bulk notification job {JobId} permanently rejected item {Sequence}.",
                    job.Id,
                    item.Sequence);
            }
        }

        job.Status = job.FailedCount == 0
            ? BulkNotificationJobStatuses.Completed
            : BulkNotificationJobStatuses.CompletedWithErrors;
        job.CompletedAtUtc = timeProvider.GetUtcNow();
        job.Error = job.FailedCount == 0
            ? null
            : "One or more notifications could not be processed.";
        await dbContext.SaveChangesAsync(cancellationToken);
        return BulkNotificationProcessDisposition.Acknowledge;
    }

    public async Task RecordRetryAsync(
        Guid jobId,
        int completedAttempts,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.BulkNotificationJobs
            .SingleOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null || job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors
            or BulkNotificationJobStatuses.DeadLettered)
        {
            return;
        }

        job.Status = BulkNotificationJobStatuses.Retrying;
        job.CompletedAtUtc = null;
        job.Error = $"Processing attempt {completedAttempts} of {maxAttempts} failed; the command remains retryable.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDeadLetteredAsync(
        Guid jobId,
        int attempts,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.BulkNotificationJobs
            .SingleOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null || job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors
            or BulkNotificationJobStatuses.DeadLettered)
        {
            return;
        }

        job.Status = BulkNotificationJobStatuses.DeadLettered;
        job.CompletedAtUtc = timeProvider.GetUtcNow();
        job.Error = $"Processing failed after {attempts} attempts; the command was routed to the dead-letter queue.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
