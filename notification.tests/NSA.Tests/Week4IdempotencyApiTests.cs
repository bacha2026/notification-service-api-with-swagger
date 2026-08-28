using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Contracts;
using NSA.Persistence;

namespace NSA.Tests;

public sealed class Week4IdempotencyApiTests
{
    [Fact]
    public async Task Bulk_endpoint_replays_the_original_response_and_conflicts_on_payload_change()
    {
        using var factory = new NsaApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string key = "bulk-http-request-123";

        using var first = await client.SendAsync(CreateBulkRequest(
            key,
            "first@example.test",
            "First"));
        using var replay = await client.SendAsync(CreateBulkRequest(
            key,
            "first@example.test",
            "First"));
        using var conflict = await client.SendAsync(CreateBulkRequest(
            key,
            "different@example.test",
            "Different"));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(
            "true",
            Assert.Single(replay.Headers.GetValues(IdempotencyContext.ReplayHeaderName)));
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(
            firstBody.RootElement.GetProperty("jobId").GetGuid(),
            replayBody.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal(
            first.Headers.Location?.OriginalString,
            replay.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Contains(
            IdempotencyContext.HeaderName,
            problem.RootElement.GetProperty("detail").GetString(),
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Single(await dbContext.BulkNotificationJobs.ToListAsync());
        Assert.Single(await dbContext.OutboxMessages.ToListAsync());
        var receipt = await dbContext.IdempotencyRecords.SingleAsync();
        Assert.Equal(64, receipt.KeyHash.Length);
        Assert.DoesNotContain(key, receipt.KeyHash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("contains whitespace")]
    public async Task Bulk_endpoint_rejects_an_invalid_idempotency_key(string key)
    {
        using var factory = new NsaApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.SendAsync(CreateBulkRequest(
            key,
            "invalid@example.test",
            "Invalid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Bulk_endpoint_rejects_an_oversized_idempotency_key()
    {
        using var factory = new NsaApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.SendAsync(CreateBulkRequest(
            new string('a', 129),
            "oversized@example.test",
            "Oversized"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Guest_checkout_replays_the_original_order_after_the_cart_is_cleared()
    {
        using var factory = new NsaApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            IdempotencyContext.HeaderName,
            "guest-checkout-request-123");

        using var first = await client.PostAsJsonAsync("/api/v2/orders", new
        {
            visitorEmail = "visitor@example.test"
        });
        using var replay = await client.PostAsJsonAsync("/api/v2/orders", new
        {
            visitorEmail = "visitor@example.test"
        });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(
            "true",
            Assert.Single(replay.Headers.GetValues(IdempotencyContext.ReplayHeaderName)));
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(
            firstBody.RootElement.GetProperty("id").GetInt32(),
            replayBody.RootElement.GetProperty("id").GetInt32());
    }

    private static HttpRequestMessage CreateBulkRequest(
        string idempotencyKey,
        string recipientEmail,
        string subject)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/notifications/bulk")
        {
            Content = JsonContent.Create(new
            {
                notifications = new[]
                {
                    new
                    {
                        recipientEmail,
                        channel = 1,
                        subject,
                        body = "Week 4 idempotency test.",
                        orderId = (int?)null
                    }
                }
            })
        };
        request.Headers.Add(IdempotencyContext.HeaderName, idempotencyKey);
        return request;
    }
}
