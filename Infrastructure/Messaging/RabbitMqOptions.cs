using System.ComponentModel.DataAnnotations;

namespace NSA.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; init; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    [Required]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string VirtualHost { get; init; } = "/";

    [Required]
    public string CommandExchange { get; init; } = "nsa.notifications.commands.v1";

    [Required]
    public string CommandQueue { get; init; } = "nsa.notifications.bulk.v1";

    [Required]
    public string CommandRoutingKey { get; init; } = "bulk.requested.v1";

    [Required]
    public string DeadLetterExchange { get; init; } = "nsa.notifications.dead-letter";

    [Required]
    public string DeadLetterQueue { get; init; } = "nsa.notifications.bulk.dlq";

    [Required]
    public string DeadLetterRoutingKey { get; init; } = "bulk.dead-letter.v1";

    [Range(1, 20)]
    public int MaxDeliveryAttempts { get; init; } = 3;

    [Range(1, 1000)]
    public int BrokerDeliveryLimit { get; init; } = 20;

    [Range(1, 1000)]
    public int PrefetchCount { get; init; } = 1;

    [Range(1, 300)]
    public int InitialConnectionRetrySeconds { get; init; } = 5;

    [Range(1, 300)]
    public int NetworkRecoverySeconds { get; init; } = 5;

    [Range(1, 30)]
    public int ConnectionTimeoutSeconds { get; init; } = 2;

    [Range(5, 600)]
    public int RequestedHeartbeatSeconds { get; init; } = 30;

    /// <summary>
    /// Optional, local-demo-only subject that forces command-level failure so the
    /// bounded retry and DLQ path can be demonstrated deterministically.
    /// </summary>
    public string? FailureInjectionSubject { get; init; }
}
