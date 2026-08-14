using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Infrastructure.Messaging;

public static class RabbitMqConnectionFactory
{
    public static ConnectionFactory Create(IConfiguration configuration) => new()
    {
        HostName = RabbitMqConfiguration.GetRequiredString(configuration, "HostName"),
        Port = RabbitMqConfiguration.GetInt32(configuration, "Port", 1, 65_535),
        UserName = RabbitMqConfiguration.GetRequiredString(configuration, "UserName"),
        Password = RabbitMqConfiguration.GetRequiredString(configuration, "Password"),
        VirtualHost = RabbitMqConfiguration.GetRequiredString(configuration, "VirtualHost"),
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(
            RabbitMqConfiguration.GetInt32(configuration, "ConnectionTimeoutSeconds", 1, 30)),
        HandshakeContinuationTimeout = TimeSpan.FromSeconds(
            RabbitMqConfiguration.GetInt32(configuration, "ConnectionTimeoutSeconds", 1, 30)),
        NetworkRecoveryInterval = TimeSpan.FromSeconds(
            RabbitMqConfiguration.GetInt32(configuration, "NetworkRecoverySeconds", 1, 300)),
        RequestedHeartbeat = TimeSpan.FromSeconds(
            RabbitMqConfiguration.GetInt32(configuration, "RequestedHeartbeatSeconds", 5, 600)),
        ConsumerDispatchConcurrency = 1
    };
}
