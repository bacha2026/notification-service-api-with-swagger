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
        // Read the exchange, queue, and routing-key names from application configuration.
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

        // Read limits that control broker redelivery and delayed recovery replay.
        var brokerDeliveryLimit = RabbitMqConfiguration.GetInt32(configuration, "BrokerDeliveryLimit", 1, 1_000);
        var replayDelayMilliseconds = RabbitMqConfiguration.GetInt32(
            configuration,
            "DeadLetterReplayDelayMilliseconds",
            0,
            3_600_000);

        // Declare all exchanges before creating their queue bindings.
        await DeclareExchangeAsync(channel, commandExchange, cancellationToken);
        await DeclareExchangeAsync(channel, deadLetterExchange, cancellationToken);
        await DeclareExchangeAsync(channel, parkingLotExchange, cancellationToken);
        await DeclareExchangeAsync(channel, recoveryExchange, cancellationToken);

        // Create the main queue that receives new notification commands.
        await channel.QueueDeclareAsync(
            commandQueue,
            // Keep the queue after a RabbitMQ restart.
            durable: true,
            // Allow consumers from multiple connections to use the queue.
            exclusive: false,
            // Do not delete the queue when consumers disconnect.
            autoDelete: false,
            // Configure replicated storage, the delivery limit, and dead-letter destination.
            arguments: new Dictionary<string, object?>
            {
                // Use RabbitMQ quorum-queue semantics for durable replicated storage.
                ["x-queue-type"] = "quorum",
                // Dead-letter the message after the broker delivery limit is reached.
                ["x-delivery-limit"] = brokerDeliveryLimit,
                // Send exhausted messages to the dead-letter exchange.
                ["x-dead-letter-exchange"] = deadLetterExchange,
                // Apply this routing key when sending the message to the dead-letter exchange.
                ["x-dead-letter-routing-key"] = deadLetterRoutingKey
            },
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        // Route command messages with the configured key from the exchange into the main queue.
        await BindAsync(channel, commandQueue, commandExchange, commandRoutingKey, cancellationToken);

        // Create the queue that stores messages rejected from the main processing path.
        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: QuorumQueueArguments(),
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        // Route dead-letter messages into the dead-letter queue.
        await BindAsync(channel, deadLetterQueue, deadLetterExchange, deadLetterRoutingKey, cancellationToken);

        // Create the parking-lot queue for messages that should not be automatically retried.
        await channel.QueueDeclareAsync(
            parkingLotQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: QuorumQueueArguments(),
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        // Route parking-lot messages into the parking-lot queue.
        await BindAsync(channel, parkingLotQueue, parkingLotExchange, parkingLotRoutingKey, cancellationToken);

        // Create the recovery queue used to delay messages before replaying them.
        await channel.QueueDeclareAsync(
            recoveryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                // Store recovery messages in a durable quorum queue.
                ["x-queue-type"] = "quorum",
                // Keep each recovery message here for the configured delay.
                ["x-message-ttl"] = replayDelayMilliseconds,
                // After the TTL expires, send the message to the command exchange.
                ["x-dead-letter-exchange"] = commandExchange,
                // Use the normal command routing key when replaying the message.
                ["x-dead-letter-routing-key"] = commandRoutingKey
            },
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
        // Allow recovery messages to enter this queue through the recovery exchange and key.
        await BindAsync(channel, recoveryQueue, recoveryExchange, recoveryRoutingKey, cancellationToken);
    }

    private static Task DeclareExchangeAsync(
        IChannel channel,
        string exchange,
        CancellationToken cancellationToken) =>
        // Use direct routing so the exchange selects queues by an exact routing-key match.
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
        // Connect a queue to an exchange for messages carrying this routing key.
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

