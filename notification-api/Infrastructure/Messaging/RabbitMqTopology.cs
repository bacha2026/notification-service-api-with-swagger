using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Infrastructure.Messaging;

/// <summary>Declares the durable command, recovery, dead-letter, and parking topology.</summary>
public static class RabbitMqTopology
{
    public static async Task DeclareAsync(
        IChannel channel,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var commandExchange = RabbitMqConfiguration.GetRequiredString(configuration, "CommandExchange");
        var commandQueue = RabbitMqConfiguration.GetRequiredString(configuration, "CommandQueue");
        var commandRoutingKey = RabbitMqConfiguration.GetRequiredString(configuration, "CommandRoutingKey");
        var deadLetterExchange = RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterExchange");
        var deadLetterQueue = RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterQueue");
        var deadLetterRoutingKey = RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterRoutingKey");
        var parkingLotExchange = RabbitMqConfiguration.GetRequiredString(configuration, "ParkingLotExchange");
        var parkingLotQueue = RabbitMqConfiguration.GetRequiredString(configuration, "ParkingLotQueue");
        var parkingLotRoutingKey = RabbitMqConfiguration.GetRequiredString(configuration, "ParkingLotRoutingKey");
        var recoveryExchange = RabbitMqConfiguration.GetRequiredString(configuration, "RecoveryExchange");
        var recoveryQueue = RabbitMqConfiguration.GetRequiredString(configuration, "RecoveryQueue");
        var recoveryRoutingKey = RabbitMqConfiguration.GetRequiredString(configuration, "RecoveryRoutingKey");
        var brokerDeliveryLimit = RabbitMqConfiguration.GetInt32(configuration, "BrokerDeliveryLimit", 1, 1_000);
        var replayDelayMilliseconds = RabbitMqConfiguration.GetInt32(
            configuration,
            "DeadLetterReplayDelayMilliseconds",
            0,
            3_600_000);

        await DeclareExchangeAsync(channel, commandExchange, cancellationToken);
        await DeclareExchangeAsync(channel, deadLetterExchange, cancellationToken);
        await DeclareExchangeAsync(channel, parkingLotExchange, cancellationToken);
        await DeclareExchangeAsync(channel, recoveryExchange, cancellationToken);

        await channel.QueueDeclareAsync(
            commandQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = brokerDeliveryLimit,
                ["x-dead-letter-exchange"] = deadLetterExchange,
                ["x-dead-letter-routing-key"] = deadLetterRoutingKey
            },
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        await BindAsync(channel, commandQueue, commandExchange, commandRoutingKey, cancellationToken);

        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: QuorumQueueArguments(),
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        await BindAsync(channel, deadLetterQueue, deadLetterExchange, deadLetterRoutingKey, cancellationToken);

        await channel.QueueDeclareAsync(
            parkingLotQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: QuorumQueueArguments(),
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        await BindAsync(channel, parkingLotQueue, parkingLotExchange, parkingLotRoutingKey, cancellationToken);

        await channel.QueueDeclareAsync(
            recoveryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = replayDelayMilliseconds,
                ["x-dead-letter-exchange"] = commandExchange,
                ["x-dead-letter-routing-key"] = commandRoutingKey
            },
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        await BindAsync(channel, recoveryQueue, recoveryExchange, recoveryRoutingKey, cancellationToken);
    }

    private static Task DeclareExchangeAsync(
        IChannel channel,
        string exchange,
        CancellationToken cancellationToken) =>
        channel.ExchangeDeclareAsync(
            exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);

    private static Task BindAsync(
        IChannel channel,
        string queue,
        string exchange,
        string routingKey,
        CancellationToken cancellationToken) =>
        channel.QueueBindAsync(
            queue,
            exchange,
            routingKey,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);

    private static Dictionary<string, object?> QuorumQueueArguments() =>
        new() { ["x-queue-type"] = "quorum" };
}
