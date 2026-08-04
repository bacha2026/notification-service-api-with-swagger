using RabbitMQ.Client;

namespace NSA.Infrastructure.Messaging;

public static class RabbitMqConnectionFactory
{
    public static ConnectionFactory Create(RabbitMqOptions options) => new()
    {
        HostName = options.HostName,
        Port = options.Port,
        UserName = options.UserName,
        Password = options.Password,
        VirtualHost = options.VirtualHost,
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(options.ConnectionTimeoutSeconds),
        HandshakeContinuationTimeout = TimeSpan.FromSeconds(options.ConnectionTimeoutSeconds),
        NetworkRecoveryInterval = TimeSpan.FromSeconds(options.NetworkRecoverySeconds),
        RequestedHeartbeat = TimeSpan.FromSeconds(options.RequestedHeartbeatSeconds),
        ConsumerDispatchConcurrency = 1
    };
}
