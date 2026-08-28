using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()
            || dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    public async Task AcquireLockAsync(
        IdempotencyContext? context,
        CancellationToken cancellationToken)
    {
        if (context is null
            || !dbContext.Database.IsSqlServer()
            || dbContext.Database.CurrentTransaction is null)
        {
            return;
        }

        var identity = $"{context.Scope}\n{context.ActorId}\n{context.Key}";
        var resource = $"nsa:idempotency:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource={resource},
                @LockMode='Exclusive',
                @LockOwner='Transaction',
                @LockTimeout=10000;
            IF @lockResult < 0
                THROW 51000, 'Could not acquire the idempotency request lock.', 1;
            """,
            cancellationToken);
    }

    public async Task PurgeExpiredAsync(
        IdempotencyContext? context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.IdempotencyRecords
                .Where(record => record.ExpiresAtUtc <= now)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var expired = await dbContext.IdempotencyRecords
            .Where(record => record.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            dbContext.IdempotencyRecords.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IdempotentResult<T>?> TryReplayAsync<T>(
        IdempotencyContext? context,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return null;
        }

        var keyHash = HashKey(context.Key);
        var existing = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
            record => record.Scope == context.Scope
                && record.ActorId == context.ActorId
                && record.KeyHash == keyHash,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            var tracked = dbContext.IdempotencyRecords.Local
                .SingleOrDefault(record => record.Id == existing.Id);
            if (dbContext.Database.IsRelational())
            {
                await dbContext.IdempotencyRecords
                    .Where(record => record.Id == existing.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                if (tracked is not null)
                {
                    dbContext.Entry(tracked).State = EntityState.Detached;
                }
            }
            else
            {
                dbContext.IdempotencyRecords.Remove(tracked ?? existing);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException(
                $"{IdempotencyContext.HeaderName} was already used for a different request in this operation.");
        }

        var value = JsonSerializer.Deserialize<T>(existing.ResponseBody, SerializerOptions)
            ?? throw new InvalidOperationException("The persisted idempotency response is invalid.");
        return new IdempotentResult<T>(value, Replayed: true);
    }

    public void Add<T>(
        IdempotencyContext? context,
        string requestHash,
        int responseStatusCode,
        T response,
        string resourceType,
        string resourceId)
    {
        if (context is null)
        {
            return;
        }

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
            ExpiresAtUtc = now.AddHours(options.IdempotencyRetentionHours)
        });
    }

    private static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
