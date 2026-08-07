using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSA.Infrastructure.Messaging;

namespace NSA.Infrastructure.Health;

public sealed class RabbitMqReadinessHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = RabbitMqConnectionFactory.Create(options.Value);
            await using var connection = await factory.CreateConnectionAsync(
                "nsa-api-readiness",
                cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ is reachable.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is not open.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ readiness probe failed.", exception);
        }
    }
}
