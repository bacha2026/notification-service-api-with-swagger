using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Persistence;

namespace NSA.Service;

public sealed class SqlServerIdempotencyLock(NotificationDbContext dbContext) : IIdempotencyLock
{
    /*
    The purpose is to serialize two simultaneous requests with the same idempotency identity:
    Request A acquires the lock and checks for an existing record.
    Request B waits.
    Request A creates the job and idempotency record, then commits.
    Request B acquires the lock, sees the existing record, and replays it instead of creating another job.

    */
    public async Task AcquireAsync(IdempotencyContext context, CancellationToken cancellationToken)
    {
        var identity = $"{context.Scope}\n{context.ActorId}\n{context.Key}";
        var resource = $"nsa:idempotency:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource={resource},
                @LockMode='Exclusive', -- only one transaction can hold this lock
                @LockOwner='Transaction', -- released when the transaction commits or rolls back
                @LockTimeout=10000; -- wait up to 10 seconds before returning a negative value
            IF @lockResult < 0
                THROW 51000, 'Could not acquire the idempotency request lock.', 1;
            """,
            cancellationToken);
    }
}
