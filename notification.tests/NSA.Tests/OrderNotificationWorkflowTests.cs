using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;
using NSA.Service;

namespace NSA.Tests;

public sealed class OrderNotificationWorkflowTests : IClassFixture<NsaApiFactory>
{
    private readonly NsaApiFactory factory;
    private readonly HttpClient client;

    public OrderNotificationWorkflowTests(NsaApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Order_creation_queues_notifications_and_the_worker_creates_in_app_records()
    {
        const string visitorEmail = "visitor@example.test";
        using var orderResponse = await client.PostAsJsonAsync("/api/v2/orders", new
        {
            visitorEmail
        });

        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        using var order = JsonDocument.Parse(await orderResponse.Content.ReadAsStringAsync());
        var orderId = order.RootElement.GetProperty("id").GetInt32();
        var jobId = Guid.Parse(Assert.Single(orderResponse.Headers.GetValues("X-Notification-Job-ID")));
        var link = Assert.Single(orderResponse.Headers.GetValues("Link"));
        Assert.Contains($"/api/v2/notifications/bulk/{jobId}", link, StringComparison.Ordinal);
        Assert.EndsWith("rel=\"notification-status\"", link, StringComparison.Ordinal);
        Assert.Equal(
            NotificationHandoffStatus.Confirmed.ToString(),
            Assert.Single(orderResponse.Headers.GetValues("X-Notification-Handoff")));
        var correlationId = Assert.Single(orderResponse.Headers.GetValues("X-Correlation-ID"));
        Assert.False(string.IsNullOrWhiteSpace(correlationId));

        using (var notificationsBeforeWorker = await client.GetAsync($"/api/v2/notifications?orderId={orderId}"))
        {
            Assert.Equal(HttpStatusCode.OK, notificationsBeforeWorker.StatusCode);
            using var beforeWorker = JsonDocument.Parse(await notificationsBeforeWorker.Content.ReadAsStringAsync());
            Assert.Empty(beforeWorker.RootElement.EnumerateArray());
        }

        using (var statusResponse = await client.GetAsync($"/api/v2/notifications/bulk/{jobId}"))
        {
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            Assert.Equal(BulkNotificationJobStatuses.Queued, status.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, status.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, status.RootElement.GetProperty("processedCount").GetInt32());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var persistedJob = await dbContext.BulkNotificationJobs
                .AsNoTracking()
                .Include(candidate => candidate.Items)
                .SingleAsync(candidate => candidate.Id == jobId);

            Assert.Equal(2, persistedJob.Items.Count);
            Assert.All(persistedJob.Items, item =>
            {
                Assert.Equal(orderId, item.OrderId);
                Assert.Equal(BulkNotificationItemStatuses.Pending, item.Status);
                Assert.Equal(NotificationChannel.InApp, item.Channel);
            });
            Assert.Contains(
                persistedJob.Items,
                item => item.RecipientEmail == "admin@example.test" && item.Subject == $"New order #{orderId}");
            Assert.Contains(
                persistedJob.Items,
                item => item.RecipientEmail == visitorEmail && item.Subject == $"Order #{orderId} received");

            var publisher = scope.ServiceProvider.GetRequiredService<IBulkNotificationCommandPublisher>();
            var testPublisher = Assert.IsType<TestBulkNotificationCommandPublisher>(publisher);
            var command = Assert.Single(testPublisher.Messages);
            Assert.Equal(BulkNotificationRequestedV1.CurrentSchemaVersion, command.SchemaVersion);
            Assert.Equal(jobId, command.JobId);
            Assert.Equal(correlationId, command.CorrelationId);
            Assert.Equal(correlationId, persistedJob.CorrelationId);

            var processor = ActivatorUtilities.CreateInstance<BulkNotificationProcessor>(scope.ServiceProvider);
            await processor.ProcessAsync(jobId, CancellationToken.None);
        }

        using var notificationsResponse = await client.GetAsync($"/api/v2/notifications?orderId={orderId}");
        Assert.Equal(HttpStatusCode.OK, notificationsResponse.StatusCode);
        using var notifications = JsonDocument.Parse(await notificationsResponse.Content.ReadAsStringAsync());
        var intents = notifications.RootElement.EnumerateArray().ToArray();

        Assert.Equal(2, intents.Length);
        Assert.All(intents, intent =>
        {
            Assert.Equal(orderId, intent.GetProperty("orderId").GetInt32());
            Assert.Equal((int)NotificationChannel.InApp, intent.GetProperty("channel").GetInt32());
            Assert.Equal(JsonValueKind.Null, intent.GetProperty("sentAtUtc").ValueKind);
        });
        Assert.Contains(
            intents,
            intent => intent.GetProperty("recipientEmail").GetString() == "admin@example.test"
                && intent.GetProperty("subject").GetString() == $"New order #{orderId}");
        Assert.Contains(
            intents,
            intent => intent.GetProperty("recipientEmail").GetString() == visitorEmail
                && intent.GetProperty("subject").GetString() == $"Order #{orderId} received");

        using var completedResponse = await client.GetAsync($"/api/v2/notifications/bulk/{jobId}");
        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        using var completed = JsonDocument.Parse(await completedResponse.Content.ReadAsStringAsync());
        Assert.Equal(BulkNotificationJobStatuses.Completed, completed.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, completed.RootElement.GetProperty("processedCount").GetInt32());
        Assert.Equal(2, completed.RootElement.GetProperty("succeededCount").GetInt32());
    }
}
