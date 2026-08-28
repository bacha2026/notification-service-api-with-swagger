using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence;

namespace NSA.Tests;

public sealed class OrderNotificationBrokerFailureTests
{
    [Fact]
    public async Task Broker_outage_does_not_prevent_durable_order_admission()
    {
        await using var factory = new NsaApiFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.PostAsJsonAsync("/api/v2/orders", new
        {
            visitorEmail = "visitor@example.test"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("id").GetInt32();
        var jobId = Guid.Parse(Assert.Single(response.Headers.GetValues("X-Notification-Job-ID")));
        Assert.Equal(
            NotificationHandoffStatus.Confirmed.ToString(),
            Assert.Single(response.Headers.GetValues("X-Notification-Handoff")));
        Assert.Contains(
            $"/api/v2/notifications/bulk/{jobId}",
            Assert.Single(response.Headers.GetValues("Link")),
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.True(await dbContext.Orders.AnyAsync(order => order.Id == orderId));

        var queuedJob = await dbContext.BulkNotificationJobs
            .AsNoTracking()
            .Include(job => job.Items)
            .SingleAsync(job => job.Id == jobId);
        Assert.Equal(BulkNotificationJobStatuses.Queued, queuedJob.Status);
        Assert.Equal(2, queuedJob.Items.Count);
        Assert.All(queuedJob.Items, item => Assert.Equal(orderId, item.OrderId));
        var outbox = await dbContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(jobId.ToString("D"), outbox.AggregateId);
        Assert.Null(outbox.PublishedAtUtc);
    }

    [Fact]
    public async Task Checkout_is_rejected_when_the_atomic_notification_admission_is_at_capacity()
    {
        await using var factory = new NsaApiFactory
        {
            BulkNotificationMaxTrackedJobs = 1
        };
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            dbContext.BulkNotificationJobs.Add(new BulkNotificationJob
            {
                Id = Guid.NewGuid(),
                Status = BulkNotificationJobStatuses.Queued,
                MessageSchemaVersion = BulkNotificationRequestedV1.CurrentSchemaVersion,
                CorrelationId = "capacity-test",
                TotalCount = 1,
                QueuedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync("/api/v2/orders", new
        {
            visitorEmail = "visitor@example.test"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Notification-Job-ID"));
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Single(await verificationContext.BulkNotificationJobs.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.OutboxMessages.AsNoTracking().ToListAsync());
    }
}
