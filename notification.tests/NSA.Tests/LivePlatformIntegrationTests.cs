using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NSA.Tests;

/// <summary>
/// Opt-in black-box test against the real Compose SQL Server/RabbitMQ boundary.
/// Normal unit-test runs skip it; the trusted live-proof job supplies the URL.
/// </summary>
public sealed class LivePlatformIntegrationTests
{
    [LivePlatformFact]
    [Trait("Category", "LiveIntegration")]
    public async Task KeyedAdmission_RelaysThroughRealSqlAndRabbit_WithOneObservedEffect()
    {
        var baseUri = GetLiveBaseUriOrSkip();
        using var client = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(15)
        };
        var key = $"xunit-live-{Guid.NewGuid():N}";
        var request = new
        {
            notifications = new[]
            {
                new
                {
                    recipientEmail = "xunit-live@example.test",
                    channel = 1,
                    subject = $"Live integration {key}",
                    body = "Real SQL, Outbox, RabbitMQ, Inbox, and worker boundary.",
                    orderId = (int?)null
                }
            }
        };

        using var first = await PostBulkAsync(client, key, request);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstBody = await ReadJsonAsync(first);
        var jobId = firstBody.RootElement.GetProperty("jobId").GetGuid();

        using var replay = await PostBulkAsync(client, key, request);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        var replayBody = await ReadJsonAsync(replay);
        Assert.Equal(jobId, replayBody.RootElement.GetProperty("jobId").GetGuid());

        using var conflict = await PostBulkAsync(client, key, new
        {
            notifications = new[]
            {
                new
                {
                    recipientEmail = "xunit-live@example.test",
                    channel = 1,
                    subject = "A changed payload must conflict",
                    body = "No second durable job is allowed for the same key.",
                    orderId = (int?)null
                }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);

        JsonDocument? terminal = null;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var status = await client.GetAsync($"api/v2/notifications/bulk/{jobId}");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            terminal?.Dispose();
            terminal = await ReadJsonAsync(status);
            var state = terminal.RootElement.GetProperty("status").GetString();
            if (state is "Completed" or "CompletedWithErrors")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.NotNull(terminal);
        using (terminal)
        {
            Assert.Equal("Completed", terminal.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, terminal.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, terminal.RootElement.GetProperty("processedCount").GetInt32());
            Assert.Equal(1, terminal.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(0, terminal.RootElement.GetProperty("failedCount").GetInt32());
        }
    }

    private static Uri GetLiveBaseUriOrSkip()
    {
        var value = Environment.GetEnvironmentVariable("NSA_LIVE_BASE_URL");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "The live test was enabled without a real SQL/RabbitMQ base URL.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("NSA_LIVE_BASE_URL must be an absolute HTTP(S) URI.");
        }

        return new Uri(uri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static async Task<HttpResponseMessage> PostBulkAsync(HttpClient client, string key, object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v2/notifications/bulk")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}

public sealed class LivePlatformFactAttribute : FactAttribute
{
    public LivePlatformFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NSA_LIVE_BASE_URL")))
        {
            Skip = "Set NSA_LIVE_BASE_URL only when the real SQL/RabbitMQ Compose stack is healthy.";
        }
    }
}
