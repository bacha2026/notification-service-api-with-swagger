using RabbitMQ.Client;

namespace NSA.Workers.Shared.Messaging;

/// <summary>
/// Broker operations for one manually acknowledged delivery. Keeping this
/// contract small makes queue-specific handlers independently testable.
/// </summary>
public interface IRabbitMqDelivery
{
    ulong DeliveryTag { get; }

    ReadOnlyMemory<byte> Body { get; }

    IReadOnlyBasicProperties Properties { get; }

    Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);

    Task AcknowledgeAsync(CancellationToken cancellationToken);

    Task RejectAsync(bool requeue, CancellationToken cancellationToken);
}
