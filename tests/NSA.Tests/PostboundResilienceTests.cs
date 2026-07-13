using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSA.Infrastructure.Email;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace NSA.Tests;

public sealed class PostboundResilienceTests
{
    [Fact]
    public async Task Transient_failures_are_retried_until_the_logical_send_succeeds()
    {
        var handler = new SequenceHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.RequestTimeout,
            HttpStatusCode.Accepted);
        using var factory = new ResilienceApiFactory(
            handler,
            retryCount: 3,
            initialRetryDelayMilliseconds: 1,
            circuitBreakerFailures: 3);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PostboundEmailSender>();

        await sender.SendAsync("person@example.com", "Eventually succeeds", "Body", CancellationToken.None);

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Non_transient_client_error_is_not_retried()
    {
        var handler = new SequenceHandler(HttpStatusCode.BadRequest);
        using var factory = new ResilienceApiFactory(
            handler,
            retryCount: 3,
            initialRetryDelayMilliseconds: 1,
            circuitBreakerFailures: 3);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PostboundEmailSender>();

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(
            "person@example.com",
            "Rejected",
            "Body",
            CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Typed_client_retries_with_exponential_backoff_then_opens_the_circuit()
    {
        var handler = new AlwaysUnavailableHandler();
        using var factory = new ResilienceApiFactory(
            handler,
            retryCount: 3,
            initialRetryDelayMilliseconds: 10,
            circuitBreakerFailures: 3);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PostboundEmailSender>();

        var stopwatch = Stopwatch.StartNew();
        for (var operation = 0; operation < 3; operation++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(
                "person@example.com",
                $"Attempt {operation}",
                "Body",
                CancellationToken.None));
        }

        var callsBeforeShortCircuit = handler.CallCount;
        var circuitException = await Record.ExceptionAsync(() => sender.SendAsync(
            "person@example.com",
            "Short circuited",
            "Body",
            CancellationToken.None));
        stopwatch.Stop();

        Assert.IsAssignableFrom<BrokenCircuitException>(circuitException);
        Assert.Equal(12, callsBeforeShortCircuit);
        Assert.Equal(callsBeforeShortCircuit, handler.CallCount);
        AssertRetrySchedule(handler.CallTimes);
        var idempotencyKeys = handler.IdempotencyKeys;
        Assert.Equal(3, idempotencyKeys.Distinct(StringComparer.Ordinal).Count());
        for (var operation = 0; operation < 3; operation++)
        {
            var logicalSendKeys = idempotencyKeys.Skip(operation * 4).Take(4).ToArray();
            Assert.Single(logicalSendKeys.Distinct(StringComparer.Ordinal));
            Assert.All(logicalSendKeys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        }
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300),
            $"Expected exponential waits before the circuit opened; elapsed {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Http_client_timeout_is_retried_as_a_transient_provider_failure()
    {
        var handler = new BlockingHandler();
        using var factory = new ResilienceApiFactory(
            handler,
            requestTimeoutSeconds: 1,
            retryCount: 1,
            initialRetryDelayMilliseconds: 1,
            circuitBreakerFailures: 10);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PostboundEmailSender>();

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => sender.SendAsync(
            "person@example.com",
            "Timeout",
            "Body",
            CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried_or_counted_as_a_provider_failure()
    {
        var handler = new BlockingHandler();
        using var factory = new ResilienceApiFactory(
            handler,
            requestTimeoutSeconds: 10,
            retryCount: 3,
            initialRetryDelayMilliseconds: 1,
            circuitBreakerFailures: 1);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PostboundEmailSender>();
        using var cancellation = new CancellationTokenSource();

        var send = sender.SendAsync("person@example.com", "Cancelled", "Body", cancellation.Token);
        await handler.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Circuit_allows_a_recovery_trial_after_the_break_period_and_can_reopen()
    {
        var handler = new SwitchableHandler();
        using var factory = new ResilienceApiFactory(
            handler,
            retryCount: 0,
            initialRetryDelayMilliseconds: 1,
            circuitBreakerFailures: 1,
            circuitBreakDurationSeconds: 1);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PostboundEmailSender>();

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(
            "person@example.com", "Open", "Body", CancellationToken.None));
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() => sender.SendAsync(
            "person@example.com", "Rejected while open", "Body", CancellationToken.None));
        Assert.Equal(1, handler.CallCount);

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));
        handler.IsAvailable = true;
        await sender.SendAsync("person@example.com", "Recovery trial", "Body", CancellationToken.None);
        Assert.Equal(2, handler.CallCount);

        handler.IsAvailable = false;
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(
            "person@example.com", "Reopen", "Body", CancellationToken.None));
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() => sender.SendAsync(
            "person@example.com", "Rejected again", "Body", CancellationToken.None));
        Assert.Equal(3, handler.CallCount);
    }

    private static void AssertRetrySchedule(IReadOnlyList<DateTimeOffset> callTimes)
    {
        Assert.Equal(12, callTimes.Count);
        for (var operation = 0; operation < 3; operation++)
        {
            var offset = operation * 4;
            Assert.True(callTimes[offset + 1] - callTimes[offset] >= TimeSpan.FromMilliseconds(15));
            Assert.True(callTimes[offset + 2] - callTimes[offset + 1] >= TimeSpan.FromMilliseconds(30));
            Assert.True(callTimes[offset + 3] - callTimes[offset + 2] >= TimeSpan.FromMilliseconds(65));
        }
    }

    private sealed class ResilienceApiFactory(
        HttpMessageHandler handler,
        int requestTimeoutSeconds = 10,
        int retryCount = 3,
        int initialRetryDelayMilliseconds = 10,
        int circuitBreakerFailures = 3,
        int circuitBreakDurationSeconds = 30) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Postbound:Enabled", "true");
            builder.UseSetting("Postbound:ApiKey", "test-key");
            builder.UseSetting("Postbound:BaseUrl", "https://postbound.test/");
            builder.UseSetting("Postbound:RequestTimeoutSeconds", requestTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("Postbound:RetryCount", retryCount.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("Postbound:InitialRetryDelayMilliseconds", initialRetryDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("Postbound:CircuitBreakerFailures", circuitBreakerFailures.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("Postbound:CircuitBreakDurationSeconds", circuitBreakDurationSeconds.ToString(CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<PostboundEmailSender>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        }
    }

    private sealed class AlwaysUnavailableHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<DateTimeOffset> callTimes = new();
        private readonly ConcurrentQueue<string?> idempotencyKeys = new();

        public int CallCount => callTimes.Count;
        public IReadOnlyList<DateTimeOffset> CallTimes => callTimes.ToArray();
        public IReadOnlyList<string?> IdempotencyKeys => idempotencyKeys.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            callTimes.Enqueue(DateTimeOffset.UtcNow);
            idempotencyKeys.Enqueue(request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);
        public Task FirstCallStarted => firstCallStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            firstCallStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            var status = statuses[Math.Min(call - 1, statuses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class SwitchableHandler : HttpMessageHandler
    {
        private int callCount;

        public bool IsAvailable { get; set; }
        public int CallCount => Volatile.Read(ref callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(
                IsAvailable ? HttpStatusCode.Accepted : HttpStatusCode.ServiceUnavailable));
        }
    }
}
