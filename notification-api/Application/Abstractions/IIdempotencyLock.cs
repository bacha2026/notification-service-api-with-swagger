using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IIdempotencyLock
{
    Task AcquireAsync(IdempotencyContext context, CancellationToken cancellationToken);
}
