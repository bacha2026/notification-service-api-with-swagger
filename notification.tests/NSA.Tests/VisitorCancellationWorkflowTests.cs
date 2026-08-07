using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;

namespace NSA.Tests;

public sealed class VisitorCancellationWorkflowTests : IClassFixture<NsaApiFactory>
{
    private readonly NsaApiFactory factory;
    private readonly HttpClient client;

    public VisitorCancellationWorkflowTests(NsaApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Visitor_cancellation_preserves_tracking_details_and_notifies_the_admin_once()
    {
        var visitorEmail = $"cancel-{Guid.NewGuid():N}@example.com";
        var orderId = await AddOrderAsync(
            visitorEmail,
            PaymentStatus.Pending,
            FulfillmentStatus.Picking,
            DeliveryStatus.WaitingForRider);

        using var firstResponse = await client.PatchAsJsonAsync($"/api/v2/orders/{orderId}/cancel", new
        {
            visitorEmail
        });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var cancelled = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        Assert.Equal((int)OrderStatus.Cancelled, cancelled.RootElement.GetProperty("orderStatus").GetInt32());
        Assert.Equal((int)PaymentStatus.Pending, cancelled.RootElement.GetProperty("paymentStatus").GetInt32());
        Assert.Equal((int)FulfillmentStatus.Picking, cancelled.RootElement.GetProperty("fulfillmentStatus").GetInt32());
        Assert.Equal((int)DeliveryStatus.WaitingForRider, cancelled.RootElement.GetProperty("deliveryStatus").GetInt32());

        using var repeatedResponse = await client.PatchAsJsonAsync($"/api/v2/orders/{orderId}/cancel", new
        {
            visitorEmail
        });
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var persistedOrder = await dbContext.Orders.AsNoTracking().SingleAsync(order => order.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, persistedOrder.OrderStatus);
        Assert.Equal(PaymentStatus.Pending, persistedOrder.PaymentStatus);
        Assert.Equal(FulfillmentStatus.Picking, persistedOrder.FulfillmentStatus);
        Assert.Equal(DeliveryStatus.WaitingForRider, persistedOrder.DeliveryStatus);

        var cancellationNotification = Assert.Single(await dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.OrderId == orderId)
            .ToListAsync());
        Assert.Equal("admin@example.test", cancellationNotification.RecipientEmail);
        Assert.Equal(NotificationChannel.InApp, cancellationNotification.Channel);
        Assert.Equal($"Order #{orderId} cancelled by visitor", cancellationNotification.Subject);
        Assert.Contains(visitorEmail, cancellationNotification.Body, StringComparison.Ordinal);
        Assert.Contains("Status: Cancelled", cancellationNotification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Visitor_cannot_cancel_an_order_owned_by_another_email()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";
        var orderId = await AddOrderAsync(
            ownerEmail,
            PaymentStatus.Unpaid,
            FulfillmentStatus.NotStarted,
            DeliveryStatus.WaitingForRider);

        using var response = await client.PatchAsJsonAsync($"/api/v2/orders/{orderId}/cancel", new
        {
            visitorEmail = $"different-{Guid.NewGuid():N}@example.com"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var persistedOrder = await dbContext.Orders.AsNoTracking().SingleAsync(order => order.Id == orderId);
        Assert.Equal(OrderStatus.Pending, persistedOrder.OrderStatus);
        Assert.False(await dbContext.Notifications.AnyAsync(notification => notification.OrderId == orderId));
    }

    [Fact]
    public async Task Admin_can_delete_every_notification_related_to_a_visitor_without_deleting_orders()
    {
        var targetEmail = $"delete-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"keep-{Guid.NewGuid():N}@example.com";
        var targetOrderId = await AddOrderAsync(
            targetEmail,
            PaymentStatus.Unpaid,
            FulfillmentStatus.NotStarted,
            DeliveryStatus.WaitingForRider);
        var otherOrderId = await AddOrderAsync(
            otherEmail,
            PaymentStatus.Paid,
            FulfillmentStatus.Packed,
            DeliveryStatus.OnTheWay);

        int[] deletedIds;
        int[] retainedIds;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var targetAdmin = Notification.Create(
                "admin@example.test", NotificationChannel.InApp, "Target admin", "Body", targetOrderId, DateTimeOffset.UtcNow);
            var targetVisitor = Notification.Create(
                targetEmail, NotificationChannel.InApp, "Target visitor", "Body", targetOrderId, DateTimeOffset.UtcNow);
            var targetDirect = Notification.Create(
                targetEmail, NotificationChannel.InApp, "Target direct", "Body", null, DateTimeOffset.UtcNow);
            var otherAdmin = Notification.Create(
                "admin@example.test", NotificationChannel.InApp, "Other admin", "Body", otherOrderId, DateTimeOffset.UtcNow);
            var otherDirect = Notification.Create(
                otherEmail, NotificationChannel.InApp, "Other direct", "Body", null, DateTimeOffset.UtcNow);
            dbContext.Notifications.AddRange(targetAdmin, targetVisitor, targetDirect, otherAdmin, otherDirect);
            await dbContext.SaveChangesAsync();
            deletedIds = [targetAdmin.Id, targetVisitor.Id, targetDirect.Id];
            retainedIds = [otherAdmin.Id, otherDirect.Id];
        }

        var uri = $"/api/v2/notifications/visitor?visitorEmail={Uri.EscapeDataString(targetEmail)}";
        using var response = await client.DeleteAsync(uri);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var repeatedResponse = await client.DeleteAsync(uri);
        Assert.Equal(HttpStatusCode.NoContent, repeatedResponse.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var remainingIds = await verificationContext.Notifications
            .AsNoTracking()
            .Select(notification => notification.Id)
            .ToListAsync();
        Assert.All(deletedIds, id => Assert.DoesNotContain(id, remainingIds));
        Assert.All(retainedIds, id => Assert.Contains(id, remainingIds));
        Assert.True(await verificationContext.Orders.AnyAsync(order => order.Id == targetOrderId));
    }

    [Theory]
    [InlineData("/api/v2/notifications/visitor")]
    [InlineData("/api/v2/notifications/visitor?visitorEmail=not-an-email")]
    public async Task Visitor_notification_deletion_requires_a_valid_email(string uri)
    {
        using var response = await client.DeleteAsync(uri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<int> AddOrderAsync(
        string visitorEmail,
        PaymentStatus paymentStatus,
        FulfillmentStatus fulfillmentStatus,
        DeliveryStatus deliveryStatus)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            VisitorEmail = visitorEmail,
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = paymentStatus,
            FulfillmentStatus = fulfillmentStatus,
            DeliveryStatus = deliveryStatus,
            TotalAmount = 100m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();
        return order.Id;
    }
}
