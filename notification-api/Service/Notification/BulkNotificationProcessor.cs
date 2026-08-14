using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;

namespace NSA.Service;

public sealed class BulkNotificationJobNotFoundException(Guid jobId)
    : Exception($"Bulk notification job {jobId} does not exist.");

/// <summary>Business outcome of processing a persisted notification job.</summary>
public enum BulkNotificationProcessingResult
{
    Completed,
    AlreadyDeadLettered
}

public sealed class BulkNotificationProcessor
{
    private readonly IBulkNotificationJobRepository jobs;
    private readonly INotificationService notificationService;
    private readonly IBulkNotificationFailureInjector failureInjector;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<BulkNotificationProcessor> logger;

    public BulkNotificationProcessor(
        IBulkNotificationJobRepository jobs,
        INotificationService notificationService,
        IBulkNotificationFailureInjector failureInjector,
        TimeProvider timeProvider,
        ILogger<BulkNotificationProcessor> logger)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        this.failureInjector = failureInjector ?? throw new ArgumentNullException(nameof(failureInjector));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BulkNotificationProcessingResult> ProcessAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetWithItemsAsync(jobId, cancellationToken)
            ?? throw new BulkNotificationJobNotFoundException(jobId);

        if (job.Status == BulkNotificationJobStatuses.DeadLettered)
        {
            logger.LogInformation(
                "Bulk notification job {JobId} is already dead-lettered and cannot be processed again.",
                job.Id);
            return BulkNotificationProcessingResult.AlreadyDeadLettered;
        }

        if (job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors)
        {
            logger.LogInformation(
                "Bulk notification job {JobId} is already terminal with status {Status}; duplicate delivery requires no processing.",
                job.Id,
                job.Status);
            return BulkNotificationProcessingResult.Completed;
        }

        // A publish failure can be ambiguous: a command may have been accepted
        // before the confirmation connection was lost. If it arrives, it is the
        // proof needed to recover the persisted job instead of discarding valid work.
        var recoveringAmbiguousPublish = job.Status == BulkNotificationJobStatuses.PublishFailed;

        failureInjector.ThrowIfTriggered(job);

        job.Status = BulkNotificationJobStatuses.Processing;
        job.StartedAtUtc ??= timeProvider.GetUtcNow();
        if (recoveringAmbiguousPublish)
        {
            job.CompletedAtUtc = null;
        }
        job.Error = null;
        await jobs.SaveChangesAsync(cancellationToken);

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
                await jobs.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (
                exception is RequestValidationException or ArgumentException
                && !cancellationToken.IsCancellationRequested)
            {
                item.Status = BulkNotificationItemStatuses.Failed;
                item.LastError = $"{exception.GetType().Name}: validation failed";
                job.ProcessedCount++;
                job.FailedCount++;
                await jobs.SaveChangesAsync(cancellationToken);
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
        await jobs.SaveChangesAsync(cancellationToken);
        return BulkNotificationProcessingResult.Completed;
    }

    public async Task RecordRetryAsync(
        Guid jobId,
        int completedAttempts,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null || job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors
            or BulkNotificationJobStatuses.DeadLettered)
        {
            return;
        }

        job.Status = BulkNotificationJobStatuses.Retrying;
        job.CompletedAtUtc = null;
        job.Error = $"Processing attempt {completedAttempts} of {maxAttempts} failed; the command remains retryable.";
        await jobs.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDeadLetteredAsync(
        Guid jobId,
        int attempts,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null || job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors
            or BulkNotificationJobStatuses.DeadLettered)
        {
            return;
        }

        job.Status = BulkNotificationJobStatuses.DeadLettered;
        job.CompletedAtUtc = timeProvider.GetUtcNow();
        job.Error = $"Processing failed after {attempts} attempts; the command was routed to the dead-letter queue.";
        await jobs.SaveChangesAsync(cancellationToken);
    }
}
