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
        var settings = options.Value;//receives RabbitMqOptions from configuration, which contains settings for retry and circuit breaker policies
        var retry = global::Polly.Policy//refers to polly namespace to avoid ambiguity with Polly.CircuitBreaker namespace
            .Handle<BrokerUnreachableException>()//RabbitMQ.Client.Exceptions.BrokerUnreachableException
            .Or<AlreadyClosedException>()//channel is closed
            .Or<IOException>()// lower level network issues
            .Or<TimeoutException>()//the broker operation timed out
            .WaitAndRetryAsync(//configures asynchronous retry policy with exponential backoff
                settings.PublishRetryCount,
                attempt => TimeSpan.FromMilliseconds(
                    settings.InitialPublishRetryDelayMilliseconds * Math.Pow(2, attempt - 1)),//exponential backoff delay calculation. retry delay increases exponentially with each attempt.
                (exception, delay, attempt, _) => logger.LogWarning(
                    exception,
                    "Retrying RabbitMQ notification command publication after {DelayMilliseconds} ms (retry {RetryAttempt} of {RetryCount})",
                    delay.TotalMilliseconds,
                    attempt,
                    settings.PublishRetryCount));

        var circuitBreaker = global::Polly.Policy //begins a second policy: the circuit breaker policy, observes the same four broker/network failure types as the retry policy.
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<IOException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(//opens the circuit after a specified number of consecutive failures, preventing further attempts for a defined duration.
                settings.PublishCircuitBreakerFailures,
                TimeSpan.FromSeconds(settings.PublishCircuitBreakDurationSeconds),//keeps the circuit open for a specified duration before allowing attempts to pass through again.
                (exception, duration) => logger.LogWarning(
                    exception,
                    "RabbitMQ notification publisher circuit opened for {DurationSeconds} seconds",
                    duration.TotalSeconds),
                () => logger.LogInformation("RabbitMQ notification publisher circuit reset"),
                () => logger.LogInformation("RabbitMQ notification publisher circuit is testing the next publication"));

        Policy = circuitBreaker.WrapAsync(retry);//combines the policies into a single resilience policy, ensuring that the retry logic is applied first, followed by the circuit breaker logic.
    }

    public IAsyncPolicy Policy { get; } //exposes the combined resilience policy, allowing other components to use it for executing RabbitMQ publish operations with the defined retry and circuit breaker behaviors.
}
