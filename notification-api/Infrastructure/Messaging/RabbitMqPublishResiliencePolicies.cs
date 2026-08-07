using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client.Exceptions;

namespace NSA.Infrastructure.Messaging;

public sealed class RabbitMqPublishResiliencePolicyProvider
{
    public RabbitMqPublishResiliencePolicyProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqBulkNotificationPublisher> logger)
    {
        var settings = options.Value;
        var retry = global::Polly.Policy
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<IOException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                settings.PublishRetryCount,
                attempt => TimeSpan.FromMilliseconds(
                    settings.InitialPublishRetryDelayMilliseconds * Math.Pow(2, attempt - 1)),
                (exception, delay, attempt, _) => logger.LogWarning(
                    exception,
                    "Retrying RabbitMQ notification command publication after {DelayMilliseconds} ms (retry {RetryAttempt} of {RetryCount})",
                    delay.TotalMilliseconds,
                    attempt,
                    settings.PublishRetryCount));

        var circuitBreaker = global::Polly.Policy
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<IOException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                settings.PublishCircuitBreakerFailures,
                TimeSpan.FromSeconds(settings.PublishCircuitBreakDurationSeconds),
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
