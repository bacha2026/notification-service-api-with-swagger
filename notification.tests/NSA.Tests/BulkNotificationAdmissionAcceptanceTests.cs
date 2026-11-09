using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence;
using Xunit.Abstractions;

namespace NSA.Tests;

public sealed class BulkNotificationAdmissionAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Bulk_admission_records_diagnostic_latency_without_broker_configuration()
    {
        using var factory = new NsaApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.AdminAccessToken);

        // Exclude host startup, endpoint JIT, and model initialization from the
        // diagnostic sample. Production SLO evidence is likewise collected from
        // an already-ready instance.
        using var warmupResponse = await client.PostAsJsonAsync("/api/v2/notifications/bulk", new
        {
            notifications = new[]
            {
                new
                {
                    recipientEmail = "latency-warmup@example.test",
                    channel = 1,
                    subject = "Warm-up admission",
                    body = "Initialize the in-process acceptance-test path.",
                    orderId = (int?)null
                }
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, warmupResponse.StatusCode);

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.PostAsJsonAsync("/api/v2/notifications/bulk", new
        {
            notifications = new[]
            {
                new
                {
                    recipientEmail = "latency-acceptance@example.test",
                    channel = 1,
                    subject = "Diagnostic admission latency",
                    body = "The HTTP path must persist this intent without broker I/O.",
                    orderId = (int?)null
                }
            }
        });
        stopwatch.Stop();

        output.WriteLine(
            "Bulk admission diagnostic: {0:F2} ms in the in-memory test host. " +
            "This measurement is retained for trend evidence; the <100 ms SLO is verified " +
            "after warm-up against representative SQL Server and is intentionally not a shared-CI wall-clock assertion.",
            stopwatch.Elapsed.TotalMilliseconds);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // The API hosts an optional queue consumer, but HTTP admission remains
        // durable through the outbox and does not publish to RabbitMQ inline.
        var accepted = await response.Content.ReadFromJsonAsync<BulkNotificationJobDto>();
        Assert.NotNull(accepted);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var outbox = await dbContext.OutboxMessages.SingleAsync(message =>
            message.AggregateId == accepted.JobId.ToString("D"));
        Assert.Null(outbox.PublishedAtUtc);
        Assert.Null(outbox.LockedBy);
        Assert.Empty(outbox.Exchange);
        Assert.Empty(outbox.RoutingKey);
    }

}
