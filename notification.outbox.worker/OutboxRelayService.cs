using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;
using NSA.Infrastructure.Configuration;
using NSA.Persistence;

namespace NSA.Outbox.Worker;

public sealed record OutboxRelayBatchResult(int Claimed, int Published, int Failed);

/// <summary>Claims due outbox rows and relays them with bounded leases and retry backoff.</summary>
public sealed class OutboxRelayService(
    NotificationDbContext dbContext,
    IOutboxMessagePublisher publisher,
    OutboxOptions options,
    TimeProvider timeProvider,
    ILogger<OutboxRelayService> logger)
{
    public async Task<OutboxRelayBatchResult> RelayDueAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        var claimedIds = await ClaimDueAsync(workerId, cancellationToken);
        var published = 0;
        var failed = 0;

        foreach (var messageId in claimedIds)
        {
            var outcome = await RelayClaimedAsync(messageId, workerId, cancellationToken);
            published += outcome ? 1 : 0;
            failed += outcome ? 0 : 1;
        }

        return new OutboxRelayBatchResult(claimedIds.Count, published, failed);
    }

    private async Task<IReadOnlyList<Guid>> ClaimDueAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var candidateIds = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAtUtc == null
                && message.NextAttemptAtUtc <= now
                && (message.LockedUntilUtc == null || message.LockedUntilUtc <= now))
            .OrderBy(message => message.NextAttemptAtUtc)
            .ThenBy(message => message.OccurredAtUtc)
            .Select(message => message.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);
        var claimedIds = new List<Guid>(candidateIds.Count);
        var lockedUntil = now.AddSeconds(options.LeaseSeconds);

        foreach (var candidateId in candidateIds)
        {
            int affected;
            if (dbContext.Database.IsRelational())
            {
                affected = await dbContext.OutboxMessages
                    .Where(message => message.Id == candidateId
                        && message.PublishedAtUtc == null
                        && message.NextAttemptAtUtc <= now
                        && (message.LockedUntilUtc == null || message.LockedUntilUtc <= now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(message => message.LockedBy, workerId)
                        .SetProperty(message => message.LockedUntilUtc, lockedUntil),
                        cancellationToken);
            }
            else
            {
                var message = await dbContext.OutboxMessages.SingleOrDefaultAsync(
                    candidate => candidate.Id == candidateId
                        && candidate.PublishedAtUtc == null
                        && candidate.NextAttemptAtUtc <= now
                        && (candidate.LockedUntilUtc == null || candidate.LockedUntilUtc <= now),
                    cancellationToken);
                affected = message is null ? 0 : 1;
                if (message is not null)
                {
                    message.LockedBy = workerId;// prevents multiple workers from claiming the same message
                    message.LockedUntilUtc = lockedUntil;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    dbContext.Entry(message).State = EntityState.Detached;
                }
            }

            if (affected == 1)
            {
                claimedIds.Add(candidateId);
            }
        }

        return claimedIds;
    }

    private async Task<bool> RelayClaimedAsync(
        Guid messageId,
        string workerId,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.OutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.Id == messageId && candidate.LockedBy == workerId,
            cancellationToken);
        if (message is null || message.PublishedAtUtc is not null)
        {
            return true;
        }

        try
        {
            await publisher.PublishAsync(message, cancellationToken);
            message.PublishedAtUtc = timeProvider.GetUtcNow();
            message.LockedBy = null;
            message.LockedUntilUtc = null;
            message.LastError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
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

    private TimeSpan CalculateBackoff(OutboxMessage message)
    {
        var exponent = Math.Min(message.AttemptCount - 1, 16);
        var baseSeconds = Math.Min(Math.Pow(2, exponent), options.MaximumBackoffSeconds);
        var deterministicJitter = 0.8 + ((uint)message.Id.GetHashCode() % 401) / 1000d;
        return TimeSpan.FromSeconds(Math.Min(baseSeconds * deterministicJitter, options.MaximumBackoffSeconds));
    }
}
