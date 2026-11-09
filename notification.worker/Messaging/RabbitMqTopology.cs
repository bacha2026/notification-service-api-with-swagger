using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Worker;

/// <summary>Declares the shared command, retry, and dead-letter topology.</summary>
public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IChannel channel, IConfiguration configuration, CancellationToken cancellationToken)
    {
        string Value(string key) => RabbitMqConfiguration.GetRequiredString(configuration, key);
        var commandExchange = Value("CommandExchange");
        var commandRoutingKey = Value("CommandRoutingKey");
        var deadLetterExchange = Value("DeadLetterExchange");
        var deadLetterRoutingKey = Value("DeadLetterRoutingKey");
        var retryExchange = Value("RetryExchange");

        var exchanges = new[] { commandExchange, deadLetterExchange, retryExchange };
        
        foreach (var exchange in exchanges)
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true,
                autoDelete: false, cancellationToken: cancellationToken);

        await DeclareQueueAsync(channel, Value("DeadLetterQueue"), deadLetterExchange,
            deadLetterRoutingKey, new() { ["x-queue-type"] = "quorum" }, cancellationToken);
        await DeclareQueueAsync(channel, Value("CommandQueue"), commandExchange, commandRoutingKey,
            new()
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = 20,
                ["x-dead-letter-exchange"] = deadLetterExchange,
                ["x-dead-letter-routing-key"] = deadLetterRoutingKey
            }, cancellationToken);

        await DeclareQueueAsync(channel, Value("RetryQueue"), retryExchange, Value("RetryRoutingKey"),
            new() { ["x-queue-type"] = "quorum", ["x-delivery-limit"] = -1 }, cancellationToken);

        var maxAttempts = RabbitMqConfiguration.GetInt32(configuration, "MaxDeliveryAttempts", 1, 20);
        var initial = RabbitMqConfiguration.GetInt32(configuration, "RetryDelayMilliseconds", 1, 3_600_000);
        var maximum = RabbitMqConfiguration.GetInt32(configuration, "MaximumRetryDelayMilliseconds", initial, 3_600_000);
        var delays = Enumerable.Range(0, maxAttempts)
            .Select(attempt => (int)Math.Min((long)initial << Math.Clamp(attempt, 0, 19), maximum))
            .Distinct();
            
        foreach (var delay in delays)
            await DeclareQueueAsync(channel, $"{Value("RetryQueue")}.delay.{delay}", retryExchange,
                $"{Value("RetryRoutingKey")}.delay.{delay}",
                new()
                {
                    ["x-queue-type"] = "quorum",
                    ["x-message-ttl"] = delay,
                    ["x-dead-letter-exchange"] = retryExchange,
                    ["x-dead-letter-routing-key"] = Value("RetryRoutingKey"),
                    ["x-dead-letter-strategy"] = "at-least-once",
                    ["x-overflow"] = "reject-publish"
                }, cancellationToken);
    }

    private static async Task DeclareQueueAsync(IChannel channel, string queue, string exchange,
        string routingKey, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: cancellationToken);
    }
}
