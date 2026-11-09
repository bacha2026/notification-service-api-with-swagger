using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Outbox.Worker;

/// <summary>Producer exchanges only; the notification consumer owns queue topology.</summary>
public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IChannel channel, IConfiguration configuration, CancellationToken cancellationToken)
    {
        foreach (var key in new[] { "CommandExchange", "RetryExchange", "DeadLetterExchange" })
            await channel.ExchangeDeclareAsync(
                RabbitMqConfiguration.GetRequiredString(configuration, key),
                ExchangeType.Direct, durable: true, autoDelete: false,
                cancellationToken: cancellationToken);
    }
}
