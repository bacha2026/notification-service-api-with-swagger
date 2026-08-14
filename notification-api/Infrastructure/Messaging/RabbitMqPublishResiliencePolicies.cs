using Microsoft.Extensions.Configuration;
using Polly;
using RabbitMQ.Client.Exceptions;

namespace NSA.Infrastructure.Messaging;

public sealed class RabbitMqPublishResiliencePolicyProvider
{
    public RabbitMqPublishResiliencePolicyProvider(
        IConfiguration configuration,
        ILogger<RabbitMqBulkNotificationPublisher> logger)
    {
        var retryCount = RabbitMqConfiguration.GetInt32(configuration, "PublishRetryCount", 0, 10);
        var initialRetryDelayMilliseconds = RabbitMqConfiguration.GetInt32(
            configuration,
            "InitialPublishRetryDelayMilliseconds",
            1,
            60_000);
        var circuitBreakerFailures = RabbitMqConfiguration.GetInt32(
            configuration,
            "PublishCircuitBreakerFailures",
            1,
            100);
        var circuitBreakDurationSeconds = RabbitMqConfiguration.GetInt32(
            configuration,
            "PublishCircuitBreakDurationSeconds",
            1,
            3_600);

        var retry = global::Polly.Policy
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<IOException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount,
                attempt => TimeSpan.FromMilliseconds(
                    initialRetryDelayMilliseconds * Math.Pow(2, attempt - 1)),
                (exception, delay, attempt, _) => logger.LogWarning(
                    exception,
                    "Retrying RabbitMQ notification command publication after {DelayMilliseconds} ms (retry {RetryAttempt} of {RetryCount})",
                    delay.TotalMilliseconds,
                    attempt,
                    retryCount));

        var circuitBreaker = global::Polly.Policy
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<IOException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                circuitBreakerFailures,
                TimeSpan.FromSeconds(circuitBreakDurationSeconds),
                (exception, duration) => logger.LogWarning(
                    exception,
                    "RabbitMQ notification publisher circuit opened for {DurationSeconds} seconds",
                    duration.TotalSeconds),
                () => logger.LogInformation("RabbitMQ notification publisher circuit reset"),
                () => logger.LogInformation("RabbitMQ notification publisher circuit is testing the next publication"));

        Policy = circuitBreaker.WrapAsync(retry);
    }

    public IAsyncPolicy Policy { get; }
}
