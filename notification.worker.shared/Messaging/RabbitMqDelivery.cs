using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Workers.Shared.Messaging;

/// <summary>Adapts RabbitMQ.Client primitives to <see cref="IRabbitMqDelivery"/>.</summary>
public sealed class RabbitMqDelivery(IChannel channel, BasicDeliverEventArgs eventArgs) : IRabbitMqDelivery
{
    public ulong DeliveryTag => eventArgs.DeliveryTag;

    public ReadOnlyMemory<byte> Body => eventArgs.Body;

    public IReadOnlyBasicProperties Properties => eventArgs.BasicProperties;

    public Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken) =>
        channel.BasicPublishAsync(
            exchange,
            routingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken).AsTask();

    public Task AcknowledgeAsync(CancellationToken cancellationToken) =>
        channel.BasicAckAsync(DeliveryTag, multiple: false, cancellationToken).AsTask();

    public Task RejectAsync(bool requeue, CancellationToken cancellationToken) =>
        channel.BasicRejectAsync(DeliveryTag, requeue, cancellationToken).AsTask();
}
