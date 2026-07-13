using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Infrastructure.Email;

namespace NSA.Tests;

public sealed class PostboundEmailSenderTests
{
    [Fact]
    public async Task Disabled_sender_logs_locally_without_calling_the_provider()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        var logger = new CapturingLogger<EmailNotificationLogger>();
        var sender = CreateSender(handler, new PostboundOptions { Enabled = false }, logger);

        var outcome = await sender.SendAsync("person@example.com", "Subject", "Body", CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.NotAttempted, outcome);
        Assert.Equal(0, handler.CallCount);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("not attempted", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("person@example.com", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Subject", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Body", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Enabled_sender_rejects_a_missing_api_key_before_calling_the_provider(string? apiKey)
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        var sender = CreateSender(handler, new PostboundOptions { Enabled = true, ApiKey = apiKey });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("person@example.com", "Subject", "Body", CancellationToken.None));

        Assert.Equal("Postbound is enabled but no API key has been configured.", exception.Message);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Enabled_sender_posts_the_expected_authenticated_json_payload()
    {
        CapturedRequest? captured = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            captured = new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Content?.Headers.ContentType?.MediaType,
                await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var sender = CreateSender(handler, new PostboundOptions { Enabled = true, ApiKey = "secret-key" });

        var outcome = await sender.SendAsync("person@example.com", "Welcome", "Hello there", CancellationToken.None);

        Assert.Equal(EmailDeliveryOutcome.AcceptedByProvider, outcome);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(new Uri("https://postbound.test/emails"), captured.RequestUri);
        Assert.Equal("Bearer", captured.AuthorizationScheme);
        Assert.Equal("secret-key", captured.AuthorizationParameter);
        Assert.True(Guid.TryParseExact(captured.IdempotencyKey, "N", out _));
        Assert.Equal("application/json", captured.MediaType);

        using var document = JsonDocument.Parse(captured.Json);
        Assert.Equal("person@example.com", document.RootElement.GetProperty("to").GetString());
        Assert.Equal("Welcome", document.RootElement.GetProperty("subject").GetString());
        Assert.Equal("Hello there", document.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Enabled_sender_surfaces_an_unsuccessful_provider_response()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var sender = CreateSender(handler, new PostboundOptions { Enabled = true, ApiKey = "secret-key" });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync("person@example.com", "Subject", "Body", CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Sender_forwards_cancellation_to_the_provider_call()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sender = CreateSender(handler, new PostboundOptions { Enabled = true, ApiKey = "secret-key" });
        using var cancellation = new CancellationTokenSource();

        var send = sender.SendAsync("person@example.com", "Subject", "Body", cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);

        Assert.Equal(1, handler.CallCount);
    }

    private static PostboundEmailSender CreateSender(
        HttpMessageHandler handler,
        PostboundOptions options,
        ILogger<EmailNotificationLogger>? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://postbound.test/")
        };
        var notificationLogger = new EmailNotificationLogger(
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailNotificationLogger>.Instance);
        return new PostboundEmailSender(httpClient, Options.Create(options), notificationLogger);
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? IdempotencyKey,
        string? MediaType,
        string Json);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory;
        private int callCount;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
            : this((request, _) => responseFactory(request))
        {
        }

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public int CallCount => Volatile.Read(ref callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return responseFactory(request, cancellationToken);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> entries = new();

        public IReadOnlyList<LogEntry> Entries => entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
