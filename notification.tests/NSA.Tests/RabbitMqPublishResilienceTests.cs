using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSA.Infrastructure.Messaging;
using Polly.CircuitBreaker;

namespace NSA.Tests;

public sealed class RabbitMqPublishResilienceTests
{
    [Fact]
    public async Task Transient_publish_failure_is_retried_and_can_recover()
    {
        var policy = CreatePolicy(retryCount: 2, breakerFailures: 3);
        var attempts = 0;

        await policy.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3
                ? Task.FromException(new IOException("temporary broker failure"))
                : Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Circuit_opens_after_configured_failed_logical_publications()
    {
        var policy = CreatePolicy(retryCount: 0, breakerFailures: 2);
        var physicalAttempts = 0;

        Task Fail(CancellationToken _)
        {
            physicalAttempts++;
            return Task.FromException(new IOException("broker unavailable"));
        }

        await Assert.ThrowsAsync<IOException>(() => policy.ExecuteAsync(Fail, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => policy.ExecuteAsync(Fail, CancellationToken.None));
        await Assert.ThrowsAsync<BrokenCircuitException>(() => policy.ExecuteAsync(Fail, CancellationToken.None));

        Assert.Equal(2, physicalAttempts);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried_or_counted_by_the_breaker()
    {
        var policy = CreatePolicy(retryCount: 2, breakerFailures: 2);
        var attempts = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() => policy.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromException(new OperationCanceledException());
        }, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    private static Polly.IAsyncPolicy CreatePolicy(int retryCount, int breakerFailures) =>
        new RabbitMqPublishResiliencePolicyProvider(
            Options.Create(new RabbitMqOptions
            {
                PublishRetryCount = retryCount,
                InitialPublishRetryDelayMilliseconds = 1,
                PublishCircuitBreakerFailures = breakerFailures,
                PublishCircuitBreakDurationSeconds = 30
            }),
            NullLogger<RabbitMqBulkNotificationPublisher>.Instance).Policy;
}
