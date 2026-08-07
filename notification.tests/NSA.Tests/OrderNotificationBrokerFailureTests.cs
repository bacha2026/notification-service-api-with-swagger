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
    public async Task Committed_order_remains_created_when_notification_publication_fails()
    {
        await using var factory = new NsaApiFactory
        {
            BulkNotificationPublishException = new InvalidOperationException("broker unavailable")
        };
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
            NotificationHandoffStatus.Unconfirmed.ToString(),
            Assert.Single(response.Headers.GetValues("X-Notification-Handoff")));
        Assert.Contains(
            $"/api/v2/notifications/bulk/{jobId}",
            Assert.Single(response.Headers.GetValues("Link")),
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.True(await dbContext.Orders.AnyAsync(order => order.Id == orderId));

        var failedJob = await dbContext.BulkNotificationJobs
            .AsNoTracking()
            .Include(job => job.Items)
            .SingleAsync(job => job.Id == jobId);
        Assert.Equal(BulkNotificationJobStatuses.PublishFailed, failedJob.Status);
        Assert.Equal(2, failedJob.Items.Count);
        Assert.All(failedJob.Items, item => Assert.Equal(orderId, item.OrderId));
    }

    [Fact]
    public async Task Committed_order_reports_rejected_handoff_when_job_store_is_at_capacity()
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

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            NotificationHandoffStatus.Rejected.ToString(),
            Assert.Single(response.Headers.GetValues("X-Notification-Handoff")));
        Assert.False(response.Headers.Contains("X-Notification-Job-ID"));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("id").GetInt32();
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.True(await verificationContext.Orders.AnyAsync(order => order.Id == orderId));
        Assert.Single(await verificationContext.BulkNotificationJobs.AsNoTracking().ToListAsync());

        var publisher = verificationScope.ServiceProvider
            .GetRequiredService<IBulkNotificationCommandPublisher>();
        Assert.Empty(Assert.IsType<TestBulkNotificationCommandPublisher>(publisher).Messages);
    }
}
