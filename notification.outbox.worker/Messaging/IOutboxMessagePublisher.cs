
namespace NSA.Outbox.Worker;

/// <summary>Confirms publication of the original payload to main, delayed retry, or DLQ.</summary>
public interface IOutboxMessagePublisher
{
    Task CheckHealthAsync(CancellationToken cancellationToken);
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
    Task PublishFailureAsync(OutboxMessage message, NotificationFailureKind kind, string errorType,
        CancellationToken cancellationToken);
}
