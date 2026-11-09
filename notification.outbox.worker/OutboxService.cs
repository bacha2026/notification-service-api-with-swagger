using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Polly.Contrib.WaitAndRetry;
namespace NSA.Outbox.Worker;

public sealed record OutboxRelayBatchResult(int Claimed, int Published, int Failed);

/// <summary>Claims due outbox rows and relays them with bounded leases and retry backoff.</summary>
public sealed class OutboxService(
    NotificationDbContext dbContext,
    IOutboxMessagePublisher publisher,
    OutboxOptions options,
    TimeProvider timeProvider,
    ILogger<OutboxService> logger)
{
    public async Task<OutboxRelayBatchResult> RelayDueAsync(
        string workerId,
        CancellationToken cancellationToken)
    {   //returns the IDs of messages that this worker successfully reserved for processing
        var claimedIds = await ClaimDueAsync(workerId, cancellationToken); 
        var published = 0;
        var failed = 0;

        foreach (var messageId in claimedIds)
        {//publishing the message and updating the outbox row accordingly, returning true if the attempt was successful and false if it failed and will be retried later.
            var outcome = await RelayClaimedAsync(messageId, workerId, cancellationToken);
            published += outcome ? 1 : 0;
            failed += outcome ? 0 : 1;
        }

        return new OutboxRelayBatchResult(claimedIds.Count, published, failed); //containing the total number of claimed messages, the number that were successfully published, and the number that failed.
    }
    
    private async Task<IReadOnlyList<Guid>> ClaimDueAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        /*
        The query filters for messages that are unpublished, due for another attempt, 
        and unlocked (or whose lease has expired). It orders them by retry time, 
        then original event time, and takes at most BatchSize. AsNoTracking() and selecting only IDs
        */
        var candidateIds = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAtUtc == null
                && message.NextAttemptAtUtc <= now
                && (message.LockedUntilUtc == null || message.LockedUntilUtc <= now))
            .OrderBy(message => message.NextAttemptAtUtc)// orders the messages by the next attempt time, so that messages that are due sooner are processed first
            .ThenBy(message => message.OccurredAtUtc)// orders the messages by the original occurrence time, so that messages that occurred earlier are processed first in case of a tie on the next attempt time
            .Select(message => message.Id)
            .Take(options.BatchSize) // limits the number of messages to process in a single batch, as defined by the BatchSize option
            .ToListAsync(cancellationToken);

        var claimedIds = new List<Guid>(candidateIds.Count);
        var lockedUntil = now.AddSeconds(options.LeaseSeconds);
        /*For each ID, a conditional database update repeats the eligibility checks and 
        sets LockedBy to workerId and LockedUntilUtc to now + LeaseSeconds.*/
        foreach (var candidateId in candidateIds)
        {
            var affected = await dbContext.OutboxMessages
                .Where(message => message.Id == candidateId
                    && message.PublishedAtUtc == null
                    && message.NextAttemptAtUtc <= now
                    && (message.LockedUntilUtc == null || message.LockedUntilUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.LockedBy, workerId)
                    .SetProperty(message => message.LockedUntilUtc, lockedUntil),//A claim is a time-limited lease, not a permanent lock. If the worker stops before finishing, another worker can claim the message after LockedUntilUtc passes
                    cancellationToken);
            /*f the update affected one row, this worker claimed it, so the ID is added to the result. 
            If another worker claimed it first—or it’s no longer eligible—the update affects zero rows 
            and it’s skipped*/
            if (affected == 1)
            {
                claimedIds.Add(candidateId);
            }
        }

        return claimedIds;
    }
    /*
        processes one message that ClaimDueAsync reserved for this worker. 
        It returns true when the message is handled—either published normally or
         successfully routed through the failure publisher—and false when the attempt 
         needs to be retried
    */
    private async Task<bool> RelayClaimedAsync(
        Guid messageId,
        string workerId,
        CancellationToken cancellationToken)
    {   //Load only a message locked by this worker
        var message = await dbContext.OutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.Id == messageId && candidate.LockedBy == workerId,
            cancellationToken);
        // If the message is not found or already published, return true to indicate it has been handled.
        if (message is null || message.PublishedAtUtc is not null) return true; 

        try
        {
            Exception? failure = null;
            try
            {

                // validates the message payload and metadata to ensure it is well-formed, consistent, and 
                // eligible for publishing. If the validation fails, it throws a PermanentMessageException, which is caught in the outer try-catch block.
                await ValidateMessageAsync(message, cancellationToken); 
                await publisher.PublishAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;//later store a description on the outbox row
                var kind = NotificationFailurePolicy.Classify(exception);
               
               // Checks whether the failure is permanent, whether the message belongs to a BulkNotificationJob, and whether its AggregateId can be parsed as a GUID. Only if all three conditions are true does it call RecordFailureAsync to mark that job as dead-lettered. The parsed GUID is stored in terminalJobId, and deadLetter: true tells the method which terminal status to record.
                if (kind == NotificationFailureKind.Permanent
                    && message.AggregateType == nameof(BulkNotificationJob)
                    && Guid.TryParse(message.AggregateId, out var terminalJobId))
                    //If the failure is permanent, the message belongs to a BulkNotificationJob, and its AggregateId can be parsed as a GUID, it calls RecordFailureAsync to mark that job as dead-lettered. The parsed GUID is stored in terminalJobId, and deadLetter: true tells the method which terminal status to record.
                    await RecordFailureAsync(terminalJobId, FailureDescription(exception), deadLetter: true, cancellationToken);
                //Routes the failed message according to its failure kind, passing the exception type name as diagnostic information. The status update happens first for permanent bulk-job failures, so if failure publication succeeds, the job’s terminal status has already been recorded
                await publisher.PublishFailureAsync(message, kind, exception.GetType().Name, cancellationToken);
            }

            message.PublishedAtUtc = timeProvider.GetUtcNow();
            message.LockedBy = null;
            message.LockedUntilUtc = null;
            message.LastError = failure is null ? null : FailureDescription(failure);

            if (failure is not null && NotificationFailurePolicy.Classify(failure) != NotificationFailureKind.Permanent
                && message.AggregateType == nameof(BulkNotificationJob)
                && Guid.TryParse(message.AggregateId, out var failedJobId))
            {
                try
                {
                    await RecordFailureAsync(failedJobId, message.LastError!, deadLetter: false, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Could not load failure status for job {JobId}; the command has been confirmed by RabbitMQ.", failedJobId);
                }
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            /*
                if the publish attempt fails at a lower level, such as a network outage or broker error, 
                or some exception escapes the validation/publish block. In that case, the row is not considered published. 
                The code clears the lock, increments the attempt count, calculates a backoff delay, sets the next retry time, 
                records the last error, saves the row, logs a warning, and returns false. That false result tells the caller that 
                this message should be retried later, not treated as completed. This is the core pattern of an idempotent, 
                retry-safe outbox relay: the row is only marked as published when the message is actually accepted by the broker
                 or deliberately routed into the retry/failure pipeline, and otherwise the row remains available for retry with exponential backoff.
                */
            message.PublishedAtUtc = null;
            message.AttemptCount++;
            message.NextAttemptAtUtc = timeProvider.GetUtcNow().Add(CalculateBackoff(message));
            message.LockedBy = null;
            message.LockedUntilUtc = null;
            message.LastError = $"{exception.GetType().Name}: broker publication failed.";
            await dbContext.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(
                exception,
                "Outbox message {MessageId} publish attempt {AttemptCount} failed; next attempt at {NextAttemptAtUtc}.",
                message.Id,
                message.AttemptCount,
                message.NextAttemptAtUtc);
            return false;
        }
    }

    private static string FailureDescription(Exception exception)
    {
        var kind = NotificationFailurePolicy.Classify(exception);
        return $"{exception.GetType().Name}: routed to {(kind == NotificationFailureKind.Permanent ? "DLQ" : "retry")} ({kind}).";
    }
    /*
     move the job into a terminal or retry state without overwriting a job that has already been completed or
      closed by another part of the system, because the job may have been completed by the consumer before the outbox relay records the failure
    */
    private async Task RecordFailureAsync(Guid jobId, string error, bool deadLetter, CancellationToken cancellationToken)
    {   //matching jobId and not terminal status
        var activeJobs = dbContext.BulkNotificationJobs.Where(job => job.Id == jobId && !BulkNotificationJobStatuses.Terminal.Contains(job.Status));

        var status = deadLetter ? BulkNotificationJobStatuses.DeadLettered : BulkNotificationJobStatuses.Retrying;

        DateTimeOffset? completedAt = deadLetter ? timeProvider.GetUtcNow() : null;

        await activeJobs.ExecuteUpdateAsync(setters => setters
            .SetProperty(job => job.Status, status)
            .SetProperty(job => job.Error, error)
            .SetProperty(job => job.CompletedAtUtc, completedAt), cancellationToken);
    }

    private async Task ValidateMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
            throw new PermanentMessageException("The outbox command payload is required.");

        var command = JsonSerializer.Deserialize<BulkNotificationRequestedV1>(message.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (command is null || command.SchemaVersion != BulkNotificationRequestedV1.CurrentSchemaVersion // ensure the payload matches the supported contract version
            || message.SchemaVersion != command.SchemaVersion || message.MessageType != BulkNotificationRequestedV1.MessageType // confirm the row metadata matches the payload and message type
            || command.MessageId == Guid.Empty || command.MessageId != message.Id || command.JobId == Guid.Empty // validate identity and job linkage so we do not publish a mismatched or orphaned command
            || !Guid.TryParse(message.AggregateId, out var aggregateId) || aggregateId != command.JobId // ensure the aggregate id matches the job id stored in the command
            || message.AggregateType != nameof(BulkNotificationJob) // only bulk notification commands are valid for this relay path
            || string.IsNullOrWhiteSpace(command.CorrelationId) || command.CorrelationId != message.CorrelationId // verify the correlation id is present and consistent across the payload and outbox row
            || command.CreatedAtUtc == default)
            throw new PermanentMessageException("The outbox command has missing, unsupported, or inconsistent metadata.");

        var job = await dbContext.BulkNotificationJobs.AsNoTracking().Include(candidate => candidate.Items) // load the job and its child items so we can validate that the command still maps to a real, current bulk notification
            .SingleOrDefaultAsync(candidate => candidate.Id == command.JobId, cancellationToken)
            ?? throw new BulkNotificationJobNotFoundException(command.JobId);

        BulkNotificationMessageValidator.Validate(job); // check the job payload is still structurally valid for processing

        if (job.CorrelationId != command.CorrelationId || job.Status == BulkNotificationJobStatuses.DeadLettered) // reject stale or dead-lettered jobs that should no longer be published
            throw new PermanentMessageException("The command does not match an eligible job.");

        var orderIds = job.Items.Where(item => item.OrderId.HasValue).Select(item => item.OrderId!.Value).Distinct().ToArray(); // gather referenced orders so we can verify they still exist before dispatch
        
        if (orderIds.Length > 0 && await dbContext.Orders.CountAsync(order => orderIds.Contains(order.Id), cancellationToken) != orderIds.Length) // ensure every referenced order is valid, otherwise the command would be invalid or inconsistent
            throw new PermanentMessageException("A notification references an order that does not exist.");
    }

    private TimeSpan CalculateBackoff(OutboxMessage message)
    {
        var exponent = Math.Min(message.AttemptCount - 1, 16);
        var delay = Backoff
            .ExponentialBackoff(TimeSpan.FromSeconds(1), exponent + 1, factor: 2)
            .ElementAt(exponent);
        return TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds, options.MaximumBackoffSeconds));
    }
}
