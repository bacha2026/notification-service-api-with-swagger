using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

/// <summary>Publishes the exact routing metadata and payload captured in an outbox row.</summary>
public interface IOutboxMessagePublisher
{
    Task CheckHealthAsync(CancellationToken cancellationToken);
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
