using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Infrastructure.Configuration;
using NSA.Persistence;

namespace NSA.Service;

/// <summary>Coordinates durable idempotency receipts inside the caller's SQL transaction.</summary>
public sealed class IdempotencyStore(
    NotificationDbContext dbContext,
    DurabilityOptions options,
    TimeProvider timeProvider,
    IIdempotencyLock idempotencyLock)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null) return null; ////avoids creating a nested or competing transaction and lets the caller’s transaction continue
        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);// helps prevent two concurrent requests with the same idempotency key from both creating separate records.
    }

    public async Task AcquireLockAsync(
        IdempotencyContext? context,
        CancellationToken cancellationToken)
    {
        if (context is null || dbContext.Database.CurrentTransaction is null) return; //avoids acquiring a lock when there is no idempotency context or when there is no active transaction
        await idempotencyLock.AcquireAsync(context, cancellationToken);
    }
    // deletes expired idempotency records to prevent stale data from affecting the current operation. This is done before acquiring the lock to avoid holding the lock for too long.
    public async Task PurgeExpiredAsync(IdempotencyContext? context, CancellationToken cancellationToken)
    {
        if (context is null) return;
        await dbContext.IdempotencyRecords.Where(record => record.ExpiresAtUtc <= timeProvider.GetUtcNow()).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IdempotentResult<T>?> TryReplayAsync<T>(IdempotencyContext? context,string requestHash,CancellationToken cancellationToken)
    {
        if (context is null) return null; // no idempotency context means no idempotency key, so there is nothing to replay

        var keyHash = HashKey(context.Key);

        var existing = await dbContext.IdempotencyRecords
            .AsNoTracking()// not tracked because we may delete it if expired
            .SingleOrDefaultAsync(
            record => record.Scope == context.Scope
                && record.ActorId == context.ActorId
                && record.KeyHash == keyHash,
            cancellationToken);

        if (existing is null) return null; // no existing record means this is the first request with this idempotency key, so there is nothing to replay

        var now = timeProvider.GetUtcNow();

        if (existing.ExpiresAtUtc <= now) // expired records are removed on the next request
        {
            var tracked = dbContext.IdempotencyRecords.Local.SingleOrDefault(record => record.Id == existing.Id);
            await dbContext.IdempotencyRecords.Where(record => record.Id == existing.Id).ExecuteDeleteAsync(cancellationToken);//deletes the database row directly, bypassing EF Core’s change tracker
           // if the record is being tracked by the change tracker, we need to detach it to avoid EF Core trying to update it later
            if (tracked is not null) dbContext.Entry(tracked).State = EntityState.Detached;//removes the existing record from the change tracker
            return null;
        }
        //If the hashes differ, the client reused the same idempotency key for different request data.
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) throw new IdempotencyConflictException($"{IdempotencyContext.HeaderName} was already used for a different request in this operation.");
        
        var value = JsonSerializer.Deserialize<T>(existing.ResponseBody, SerializerOptions) ?? throw new InvalidOperationException("The persisted idempotency response is invalid.");
        return new IdempotentResult<T>(value, Replayed: true); //returns the stored response and marks it as replayed. The caller can then return that response without executing the original operation again
    }

    public void Add<T>(
        IdempotencyContext? context,
        string requestHash,
        int responseStatusCode,
        T response,
        string resourceType,
        string resourceId)
    {
        if (context is null) return; // no idempotency context means no idempotency key, so there is nothing to store

        var now = timeProvider.GetUtcNow();

        dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Scope = context.Scope,
            ActorId = context.ActorId,
            KeyHash = HashKey(context.Key),
            RequestHash = requestHash,
            ResponseStatusCode = responseStatusCode,
            ResponseBody = JsonSerializer.Serialize(response, SerializerOptions),
            ResourceType = resourceType,
            ResourceId = resourceId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(options.IdempotencyRetentionHours) // expires after the configured retention period to prevent stale data from affecting future requests
        });
    }

    private static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
