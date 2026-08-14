using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace NSA.Infrastructure.Messaging;

/// <summary>
/// Reads and validates the <c>RabbitMq</c> section directly from application
/// configuration. This deliberately avoids binding broker settings to a POCO so
/// every runtime value remains visibly sourced from <c>appsettings.json</c> or
/// an overriding configuration provider.
/// </summary>
public static class RabbitMqConfiguration
{
    public const string SectionName = "RabbitMq";

    private static readonly string[] RequiredStringKeys =
    [
        "HostName",
        "UserName",
        "Password",
        "VirtualHost",
        "CommandExchange",
        "CommandQueue",
        "CommandRoutingKey",
        "DeadLetterExchange",
        "DeadLetterQueue",
        "DeadLetterRoutingKey",
        "ParkingLotExchange",
        "ParkingLotQueue",
        "ParkingLotRoutingKey",
        "RecoveryExchange",
        "RecoveryQueue",
        "RecoveryRoutingKey"
    ];

    private static readonly IntegerConstraint[] IntegerConstraints =
    [
        new("Port", 1, 65_535),
        new("MaxDeliveryAttempts", 1, 20),
        new("MaxDeadLetterReplayAttempts", 1, 20),
        new("DeadLetterReplayDelayMilliseconds", 0, 3_600_000),
        new("BrokerDeliveryLimit", 1, 1_000),
        new("PrefetchCount", 1, 1_000),
        new("InitialConnectionRetrySeconds", 1, 300),
        new("NetworkRecoverySeconds", 1, 300),
        new("ConnectionTimeoutSeconds", 1, 30),
        new("RequestedHeartbeatSeconds", 5, 600),
        new("PublishRetryCount", 0, 10),
        new("InitialPublishRetryDelayMilliseconds", 1, 60_000),
        new("PublishCircuitBreakerFailures", 1, 100),
        new("PublishCircuitBreakDurationSeconds", 1, 3_600)
    ];

    /// <summary>Fails fast when the configured RabbitMQ section is incomplete or out of range.</summary>
    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var key in RequiredStringKeys)
        {
            _ = GetRequiredString(configuration, key);
        }

        foreach (var constraint in IntegerConstraints)
        {
            _ = GetInt32(configuration, constraint.Key, constraint.Minimum, constraint.Maximum);
        }
    }

    public static string GetRequiredString(IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var value = configuration[$"{SectionName}:{key}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration value '{SectionName}:{key}' is required and cannot be blank.");
        }

        return value;
    }

    public static string? GetOptionalString(IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return configuration[$"{SectionName}:{key}"];
    }

    public static int GetInt32(IConfiguration configuration, string key, int minimum, int maximum)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var configuredValue = configuration[$"{SectionName}:{key}"];
        if (!int.TryParse(
                configuredValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            || value < minimum
            || value > maximum)
        {
            throw new InvalidOperationException(
                $"Configuration value '{SectionName}:{key}' must be an integer between {minimum} and {maximum}.");
        }

        return value;
    }

    private readonly record struct IntegerConstraint(string Key, int Minimum, int Maximum);
}
