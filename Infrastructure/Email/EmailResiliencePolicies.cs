using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace NSA.Infrastructure.Email;

public static class EmailResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy(
        PostboundOptions options,
        ILogger<PostboundEmailSender> logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: options.CircuitBreakerFailures,
                durationOfBreak: TimeSpan.FromSeconds(options.CircuitBreakDurationSeconds),
                onBreak: (outcome, duration) => logger.LogWarning(
                    "Email provider circuit opened for {DurationSeconds} seconds after {Failure}",
                    duration.TotalSeconds,
                    DescribeFailure(outcome)),
                onReset: () => logger.LogInformation("Email provider circuit reset"),
                onHalfOpen: () => logger.LogInformation("Email provider circuit is testing the next request"));
    }

    public static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(
        PostboundOptions options,
        ILogger<PostboundEmailSender> logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                retryCount: options.RetryCount,
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
                    options.InitialRetryDelayMilliseconds * Math.Pow(2, retryAttempt)),
                onRetry: (outcome, delay, retryAttempt, _) => logger.LogWarning(
                    "Retrying email provider request after {DelayMilliseconds} ms (attempt {RetryAttempt}) because of {Failure}",
                    delay.TotalMilliseconds,
                    retryAttempt,
                    DescribeFailure(outcome)));
    }

    public static IAsyncPolicy<HttpResponseMessage> CreateTimeoutPolicy(PostboundOptions options)
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            TimeoutStrategy.Optimistic);
    }

    private static string DescribeFailure(DelegateResult<HttpResponseMessage> outcome)
    {
        return outcome.Exception?.GetType().Name
            ?? $"HTTP {(int?)outcome.Result?.StatusCode}";
    }
}

/// <summary>
/// Owns one shared policy set for the typed client. Circuit state must be shared across
/// requests; constructing a breaker for every request would prevent it from ever opening.
/// </summary>
public sealed class EmailResiliencePolicyProvider
{
    public EmailResiliencePolicyProvider(
        IOptions<PostboundOptions> options,
        ILogger<PostboundEmailSender> logger)
    {
        CircuitBreaker = EmailResiliencePolicies.CreateCircuitBreakerPolicy(options.Value, logger);
        Retry = EmailResiliencePolicies.CreateRetryPolicy(options.Value, logger);
        Timeout = EmailResiliencePolicies.CreateTimeoutPolicy(options.Value);
    }

    public IAsyncPolicy<HttpResponseMessage> CircuitBreaker { get; }
    public IAsyncPolicy<HttpResponseMessage> Retry { get; }
    public IAsyncPolicy<HttpResponseMessage> Timeout { get; }
}
