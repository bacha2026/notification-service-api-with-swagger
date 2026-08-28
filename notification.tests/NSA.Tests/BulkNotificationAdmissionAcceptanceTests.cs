using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence;
using Xunit.Abstractions;

namespace NSA.Tests;

public sealed class BulkNotificationAdmissionAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Bulk_admission_records_diagnostic_latency_and_never_resolves_a_broker_publisher()
    {
        var brokerProbe = new BrokerAccessProbe();
        using var baseFactory = new NsaApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxMessagePublisher>();
                services.AddSingleton<IOutboxMessagePublisher>(_ => brokerProbe.ResolvePublisher());
            }));
        using var client = factory.CreateClient();

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
        Assert.Equal(0, brokerProbe.ResolutionCount);
        Assert.Equal(0, brokerProbe.CallCount);
        var accepted = await response.Content.ReadFromJsonAsync<BulkNotificationJobDto>();
        Assert.NotNull(accepted);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var outbox = await dbContext.OutboxMessages.SingleAsync(message =>
            message.AggregateId == accepted.JobId.ToString("D"));
        Assert.Null(outbox.PublishedAtUtc);
        Assert.Null(outbox.LockedBy);
    }

    private sealed class BrokerAccessProbe
    {
        private int resolutionCount;
        private int callCount;

        public int ResolutionCount => Volatile.Read(ref resolutionCount);

        public int CallCount => Volatile.Read(ref callCount);

        public IOutboxMessagePublisher ResolvePublisher()
        {
            Interlocked.Increment(ref resolutionCount);
            return new FailOnUsePublisher(this);
        }

        private sealed class FailOnUsePublisher(BrokerAccessProbe owner) : IOutboxMessagePublisher
        {
            public Task CheckHealthAsync(CancellationToken cancellationToken) => Fail();

            public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken) => Fail();

            private Task Fail()
            {
                Interlocked.Increment(ref owner.callCount);
                throw new InvalidOperationException(
                    "The API bulk-admission path must not perform RabbitMQ I/O.");
            }
        }
    }
}
