using NSA.Application.Abstractions;
using NSA.Domain.Entities;

namespace NSA.Service;

/// <summary>
/// Makes a persisted bulk-notification job eligible for a controlled replay
/// after its delivery has been dead-lettered.
/// </summary>
public sealed class BulkNotificationDeadLetterRecoveryService(
    IBulkNotificationJobRepository jobs,
    TimeProvider timeProvider,
    ILogger<BulkNotificationDeadLetterRecoveryService> logger)
{
    public async Task<BulkNotificationDeadLetterRecoveryResult> PrepareForReplayAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            logger.LogWarning(
                "Dead-lettered delivery references unknown bulk notification job {JobId}.",
                jobId);
            return BulkNotificationDeadLetterRecoveryResult.JobNotFound;
        }

        if (job.Status is BulkNotificationJobStatuses.Completed
            or BulkNotificationJobStatuses.CompletedWithErrors)
        {
            logger.LogInformation(
                "Dead-lettered delivery for already completed bulk notification job {JobId} requires no recovery.",
                jobId);
            return BulkNotificationDeadLetterRecoveryResult.NoRecoveryRequired;
        }

        if (job.Status == BulkNotificationJobStatuses.DeadLettered)
        {
            var now = timeProvider.GetUtcNow();
            job.Status = BulkNotificationJobStatuses.RecoveryPending;
            job.QueuedAtUtc = now;
            job.StartedAtUtc = null;
            job.CompletedAtUtc = null;
            job.Error = "Requeued by the dead-letter recovery worker.";
            await jobs.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Marked dead-lettered bulk notification job {JobId} as pending recovery.",
                jobId);
        }

        return job.Status == BulkNotificationJobStatuses.RecoveryPending
            ? BulkNotificationDeadLetterRecoveryResult.RecoveryPrepared
            : BulkNotificationDeadLetterRecoveryResult.NoRecoveryRequired;
    }
}

/// <summary>Business outcome of evaluating a dead-lettered notification job.</summary>
public enum BulkNotificationDeadLetterRecoveryResult
{
    RecoveryPrepared,
    NoRecoveryRequired,
    JobNotFound
}
