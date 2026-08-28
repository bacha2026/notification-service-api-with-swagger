using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Contracts;
using NSA.Persistence;
using NSA.Service;

namespace NSA.Tests;

/// <summary>
/// Verifies the authenticated order boundary without changing the established
/// guest order routes used by the pre-account storefront flow.
/// </summary>
public sealed class AuthenticatedOrderWorkflowTests
{
    [Fact]
    public async Task Authenticated_cart_routes_are_bound_to_the_bearer_subject_and_cannot_mutate_another_cart()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var firstAccount = await RegisterAsync(client, "cart-first");
        var secondAccount = await RegisterAsync(client, "cart-second");

        var firstCartItemId = await AddAuthenticatedCartItemAsync(client, firstAccount, productId: 1);
        _ = await AddAuthenticatedCartItemAsync(client, secondAccount, productId: 2);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstAccount.AccessToken);
        using var firstCartResponse = await client.GetAsync("/api/v2/auth/cart");
        Assert.Equal(HttpStatusCode.OK, firstCartResponse.StatusCode);
        using var firstCart = JsonDocument.Parse(await firstCartResponse.Content.ReadAsStringAsync());
        var firstItem = Assert.Single(firstCart.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(firstCartItemId, firstItem.GetProperty("id").GetInt32());
        Assert.Equal(1, firstItem.GetProperty("productId").GetInt32());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondAccount.AccessToken);
        using var secondCartResponse = await client.GetAsync("/api/v2/auth/cart");
        Assert.Equal(HttpStatusCode.OK, secondCartResponse.StatusCode);
        using var secondCart = JsonDocument.Parse(await secondCartResponse.Content.ReadAsStringAsync());
        var secondItem = Assert.Single(secondCart.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(2, secondItem.GetProperty("productId").GetInt32());
        Assert.NotEqual(firstCartItemId, secondItem.GetProperty("id").GetInt32());

        using var updateOtherResponse = await client.PutAsJsonAsync(
            $"/api/v2/auth/cart/items/{firstCartItemId}",
            new { quantity = 5 });
        using var deleteOtherResponse = await client.DeleteAsync($"/api/v2/auth/cart/items/{firstCartItemId}");

        // Both mutation attempts use an item id that exists, but not for this
        // JWT subject; return 404 rather than revealing another cart's state.
        Assert.Equal(HttpStatusCode.NotFound, updateOtherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteOtherResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstAccount.AccessToken);
        using var verifiedFirstCartResponse = await client.GetAsync("/api/v2/auth/cart");
        Assert.Equal(HttpStatusCode.OK, verifiedFirstCartResponse.StatusCode);
        using var verifiedFirstCart = JsonDocument.Parse(await verifiedFirstCartResponse.Content.ReadAsStringAsync());
        var verifiedItem = Assert.Single(verifiedFirstCart.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(firstCartItemId, verifiedItem.GetProperty("id").GetInt32());
        Assert.Equal(1, verifiedItem.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Authenticated_order_routes_bind_orders_to_the_bearer_subject_and_isolate_accounts()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var firstAccount = await RegisterAsync(client, "first");
        var secondAccount = await RegisterAsync(client, "second");

        await AddCartItemAsync(client, firstAccount.Email, productId: 1);
        await AddCartItemAsync(client, secondAccount.Email, productId: 2);

        var firstOrderId = await CreateAuthenticatedOrderAsync(client, firstAccount);
        var secondOrderId = await CreateAuthenticatedOrderAsync(client, secondAccount);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstAccount.AccessToken);
        using var firstHistoryResponse = await client.GetAsync("/api/v2/auth/orders");
        using var secondOrderResponse = await client.GetAsync($"/api/v2/auth/orders/{secondOrderId}");

        Assert.Equal(HttpStatusCode.OK, firstHistoryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, secondOrderResponse.StatusCode);
        using var firstHistory = JsonDocument.Parse(await firstHistoryResponse.Content.ReadAsStringAsync());
        var orders = firstHistory.RootElement.EnumerateArray().ToArray();
        var onlyOrder = Assert.Single(orders);
        Assert.Equal(firstOrderId, onlyOrder.GetProperty("id").GetInt32());
        Assert.Equal(firstAccount.Email, onlyOrder.GetProperty("visitorEmail").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var persistedOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == firstOrderId || order.Id == secondOrderId)
            .ToListAsync();

        Assert.Equal(firstAccount.UserId, Assert.Single(persistedOrders, order => order.Id == firstOrderId).UserId);
        Assert.Equal(secondAccount.UserId, Assert.Single(persistedOrders, order => order.Id == secondOrderId).UserId);
    }

    [Fact]
    public async Task Auth_notifications_require_the_bearer_recipient_and_a_completed_succeeded_job_item()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var firstAccount = await RegisterAsync(client, "notifications-first");
        var secondAccount = await RegisterAsync(client, "notifications-second");

        await AddCartItemAsync(client, firstAccount.Email, productId: 1);
        await AddCartItemAsync(client, secondAccount.Email, productId: 2);
        var firstOrderId = await CreateAuthenticatedOrderAsync(client, firstAccount);
        var secondOrderId = await CreateAuthenticatedOrderAsync(client, secondAccount);

        // Direct records have no completed job/item association and must not
        // enter the authenticated feed merely by reusing the signed-in email.
        client.DefaultRequestHeaders.Authorization = null;
        await CreateNotificationAsync(client, firstAccount.Email, "Owned order update", firstOrderId);
        await CreateNotificationAsync(client, firstAccount.Email, "Standalone email update", orderId: null);
        await CreateNotificationAsync(client, "admin@example.test", "Admin order copy", firstOrderId);
        await CreateNotificationAsync(client, firstAccount.Email, "Other account order", secondOrderId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var firstJobId = await dbContext.BulkNotificationJobItems
                .Where(item => item.OrderId == firstOrderId)
                .Select(item => item.JobId)
                .Distinct()
                .SingleAsync();
            await ActivatorUtilities.CreateInstance<BulkNotificationProcessor>(scope.ServiceProvider)
                .ProcessAsync(firstJobId, CancellationToken.None);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstAccount.AccessToken);
        using var response = await client.GetAsync("/api/v2/auth/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var notificationDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var notifications = notificationDocument.RootElement.EnumerateArray().ToArray();
        var notification = Assert.Single(notifications);
        Assert.Equal($"Order #{firstOrderId} received", notification.GetProperty("subject").GetString());
        Assert.Equal(firstOrderId, notification.GetProperty("orderId").GetInt32());
        Assert.Equal(firstAccount.Email, notification.GetProperty("recipientEmail").GetString());

        var notificationId = notification.GetProperty("id").GetInt32();
        using var markRead = await client.PatchAsync($"/api/v2/auth/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.OK, markRead.StatusCode);
        using var readBody = JsonDocument.Parse(await markRead.Content.ReadAsStringAsync());
        Assert.True(readBody.RootElement.GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Authenticated_checkout_replays_the_same_order_for_the_same_idempotency_key()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var account = await RegisterAsync(client, "idempotent-checkout");
        await AddAuthenticatedCartItemAsync(client, account, productId: 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        client.DefaultRequestHeaders.Add(IdempotencyContext.HeaderName, "checkout-request-123");

        using var first = await client.PostAsync("/api/v2/auth/orders", content: null);
        using var replay = await client.PostAsync("/api/v2/auth/orders", content: null);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.False(first.Headers.Contains(IdempotencyContext.ReplayHeaderName));
        Assert.Equal(
            "true",
            Assert.Single(replay.Headers.GetValues(IdempotencyContext.ReplayHeaderName)));
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(
            firstBody.RootElement.GetProperty("id").GetInt32(),
            replayBody.RootElement.GetProperty("id").GetInt32());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Single(await dbContext.Orders.Where(order => order.UserId == account.UserId).ToListAsync());
        Assert.Single(await dbContext.BulkNotificationJobs.Where(
            job => job.Items.Any(item => item.RecipientEmail == account.Email)).ToListAsync());
        Assert.Single(await dbContext.OutboxMessages.ToListAsync());
        var receipt = await dbContext.IdempotencyRecords.SingleAsync();
        Assert.DoesNotContain("checkout-request-123", receipt.KeyHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_completed_notification_keeps_the_shared_job_and_other_recipient_notification()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var account = await RegisterAsync(client, "delete-durable");
        await AddAuthenticatedCartItemAsync(client, account, productId: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        client.DefaultRequestHeaders.Add(IdempotencyContext.HeaderName, "delete-request-123");

        using var orderResponse = await client.PostAsync("/api/v2/auth/orders", content: null);
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var jobId = Guid.Parse(Assert.Single(orderResponse.Headers.GetValues("X-Notification-Job-ID")));
        Guid messageId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var outbox = await dbContext.OutboxMessages.SingleAsync(
                message => message.AggregateId == jobId.ToString("D"));
            var command = JsonSerializer.Deserialize<BulkNotificationRequestedV1>(
                outbox.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(command);
            messageId = command.MessageId;
            await ActivatorUtilities.CreateInstance<BulkNotificationProcessor>(scope.ServiceProvider)
                .ProcessAsync(messageId, jobId, CancellationToken.None);
        }

        using var feedResponse = await client.GetAsync("/api/v2/auth/notifications");
        Assert.Equal(HttpStatusCode.OK, feedResponse.StatusCode);
        using var feed = JsonDocument.Parse(await feedResponse.Content.ReadAsStringAsync());
        var notificationId = Assert.Single(feed.RootElement.EnumerateArray()).GetProperty("id").GetInt32();
        using var deleteResponse = await client.DeleteAsync(
            $"/api/v2/auth/notifications/{notificationId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await verification.Notifications.AnyAsync(notification => notification.Id == notificationId));
        Assert.True(await verification.Notifications.AnyAsync(notification =>
            notification.BulkNotificationJobId == jobId
            && notification.RecipientEmail != account.Email));
        Assert.True(await verification.BulkNotificationJobs.AnyAsync(job => job.Id == jobId));
        Assert.True(await verification.BulkNotificationJobItems.AnyAsync(item => item.JobId == jobId));
        Assert.True(await verification.OutboxMessages.AnyAsync(
            message => message.AggregateId == jobId.ToString("D")));
        Assert.True(await verification.IdempotencyRecords.AnyAsync());
        Assert.True(await verification.InboxMessages.AnyAsync(
            receipt => receipt.MessageId == messageId));
    }

    private static HttpClient CreateClient(NsaApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<AuthenticatedAccount> RegisterAsync(HttpClient client, string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsJsonAsync("/api/v2/auth/signup", new
        {
            email,
            password = "SafePassword!123",
            firstName = "Taylor",
            lastName = "Customer",
            gender = 1
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new AuthenticatedAccount(
            Guid.Parse(document.RootElement.GetProperty("user").GetProperty("id").GetString()!),
            email,
            document.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task AddCartItemAsync(HttpClient client, string email, int productId)
    {
        using var response = await client.PostAsJsonAsync("/api/v2/cart/items", new
        {
            visitorEmail = email,
            productId,
            quantity = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<int> AddAuthenticatedCartItemAsync(
        HttpClient client,
        AuthenticatedAccount account,
        int productId)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await client.PostAsJsonAsync("/api/v2/auth/cart/items", new
        {
            productId,
            quantity = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.Single(document.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("id")
            .GetInt32();
    }

    private static async Task<int> CreateAuthenticatedOrderAsync(HttpClient client, AuthenticatedAccount account)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await client.PostAsync("/api/v2/auth/orders", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderId = document.RootElement.GetProperty("id").GetInt32();
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/api/v2/auth/orders/{orderId}", GetLocationPath(response.Headers.Location!));
        return orderId;
    }

    private static async Task CreateNotificationAsync(
        HttpClient client,
        string recipientEmail,
        string subject,
        int? orderId)
    {
        using var response = await client.PostAsJsonAsync("/api/v2/notifications", new
        {
            recipientEmail,
            channel = 1,
            subject,
            body = "Order notification test content.",
            orderId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string GetLocationPath(Uri location) =>
        location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?', '#')[0];

    private sealed record AuthenticatedAccount(Guid UserId, string Email, string AccessToken);
}
