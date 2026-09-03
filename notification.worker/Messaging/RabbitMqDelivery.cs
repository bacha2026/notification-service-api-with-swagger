using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Worker.Messaging;

/// <summary>Adapts RabbitMQ.Client primitives to <see cref="IRabbitMqDelivery"/>.</summary>
public sealed class RabbitMqDelivery(IChannel channel, BasicDeliverEventArgs eventArgs) : IRabbitMqDelivery
{
    public ulong DeliveryTag => eventArgs.DeliveryTag;

    public ReadOnlyMemory<byte> Body => eventArgs.Body; // payload bytes from the queue

    public IReadOnlyBasicProperties Properties => eventArgs.BasicProperties; // metadata from the queue, including headers

    public Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken) =>
        channel.BasicPublishAsync(
            exchange,
            routingKey,
            mandatory: true,//broker must confirm the message has a route, if not, it will be returned as undeliverable
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken).AsTask();

    public Task AcknowledgeAsync(CancellationToken cancellationToken) =>
        channel.BasicAckAsync(DeliveryTag, multiple: false, cancellationToken).AsTask();

    public Task RejectAsync(bool requeue, CancellationToken cancellationToken) =>
        channel.BasicRejectAsync(DeliveryTag, requeue, cancellationToken).AsTask();
}

