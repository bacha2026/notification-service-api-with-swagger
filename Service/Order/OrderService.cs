using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace NSA.Service;

public sealed class OrderService(
    IOrderRepository orderRepository,
    INotificationDispatcher notificationDispatcher,
    IConfiguration configuration,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        var email = ResolveVisitorEmail(visitorEmail);
        var orders = await orderRepository.GetOrdersForVisitorAsync(email, cancellationToken);

        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderDto?> GetOrderAsync(int id, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, cancellationToken);
        return order is null ? null : ToDto(order);
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var visitorEmail = ResolveVisitorEmail(request.VisitorEmail);
        var cartItems = await orderRepository.GetCartItemsForOrderAsync(visitorEmail, cancellationToken);
        if (cartItems.Count == 0)
        {
            throw new RequestValidationException("The cart is empty.");
        }

        var order = Order.CreateFromCart(visitorEmail, cartItems, DateTimeOffset.UtcNow);
        orderRepository.Add(order);
        orderRepository.RemoveCartItems(cartItems);
        await orderRepository.SaveChangesAsync(cancellationToken);

        var body = BuildOrderMessage(order);
        await CreateEmailNotificationSafelyAsync(AdminEmail, $"New order #{order.Id}", body, order.Id, cancellationToken);
        await CreateEmailNotificationSafelyAsync(visitorEmail, $"Order #{order.Id} received", body, order.Id, cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        return ToDto(order);
    }

    public async Task<OrderDto?> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.UpdateStatuses(request.OrderStatus, request.PaymentStatus, request.FulfillmentStatus, request.DeliveryStatus, DateTimeOffset.UtcNow);

        var body = BuildOrderMessage(order);
        await CreateEmailNotificationSafelyAsync(AdminEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await CreateEmailNotificationSafelyAsync(order.VisitorEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        return ToDto(order);
    }

    private string AdminEmail => configuration["NotificationEmails:AdminEmail"] ?? "bmacha2026@gmail.com";

    private async Task CreateEmailNotificationSafelyAsync(
        string recipientEmail,
        string subject,
        string body,
        int orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            await notificationDispatcher.CreateEmailNotificationAsync(recipientEmail, subject, body, orderId, cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            // NotificationDispatcher persists the unsent intent before calling the provider.
            // Week 3/4 replaces this best-effort handoff with a broker and transactional Outbox.
            logger.LogWarning(exception, "Order {OrderId} was saved, but an email notification remains pending", orderId);
        }
    }

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException
            or BrokenCircuitException
            or TimeoutRejectedException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private string ResolveVisitorEmail(string visitorEmail)
    {
        return string.IsNullOrWhiteSpace(visitorEmail)
            ? configuration["NotificationEmails:DefaultVisitorEmail"] ?? "bmacha2015@gmail.com"
            : visitorEmail.Trim();
    }

    private static OrderDto ToDto(Order order)
    {
        return new OrderDto(
            order.Id,
            order.VisitorEmail,
            order.OrderStatus,
            order.PaymentStatus,
            order.FulfillmentStatus,
            order.DeliveryStatus,
            order.TotalAmount,
            order.CreatedAtUtc,
            order.Items.Select(item => new OrderItemDto(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.Subtotal)).ToList());
    }

    private static string BuildOrderMessage(Order order)
    {
        var lines = order.Items.Select(item => $"{item.ProductName} x {item.Quantity} @ {item.UnitPrice:C} = {item.Subtotal:C}");
        return $"Order #{order.Id} for {order.VisitorEmail}. Status: {order.OrderStatus}; Payment: {order.PaymentStatus}; Fulfillment: {order.FulfillmentStatus}; Delivery: {order.DeliveryStatus}. Total: {order.TotalAmount:C}. Items: {string.Join("; ", lines)}";
    }
}
