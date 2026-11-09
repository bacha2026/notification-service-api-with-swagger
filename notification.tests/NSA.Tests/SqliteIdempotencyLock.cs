using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Tests;

// SQLite's serializable write transaction already locks the database. SQL Server's
// transaction-owned application lock is verified by the live integration tests.
internal sealed class SqliteIdempotencyLock : IIdempotencyLock
{
    public Task AcquireAsync(IdempotencyContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}
