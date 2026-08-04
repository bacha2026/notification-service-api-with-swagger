using RabbitMQ.Client;

namespace NSA.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public static async Task DeclareAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            options.CommandExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            options.DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);

        var commandArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = options.BrokerDeliveryLimit,
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = options.DeadLetterRoutingKey
        };
        await channel.QueueDeclareAsync(
            options.CommandQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: commandArguments,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            options.CommandQueue,
            options.CommandExchange,
            options.CommandRoutingKey,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);

        var deadLetterArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum"
        };
        await channel.QueueDeclareAsync(
            options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: deadLetterArguments,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            options.DeadLetterQueue,
            options.DeadLetterExchange,
            options.DeadLetterRoutingKey,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);
    }
}
