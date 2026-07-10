using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class OrderService(IOrderRepository orderRepository, INotificationDispatcher notificationDispatcher, IConfiguration configuration) : IOrderService
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
        var order = Order.CreateFromCart(visitorEmail, cartItems, DateTimeOffset.UtcNow);
        orderRepository.Add(order);
        orderRepository.RemoveCartItems(cartItems);
        await orderRepository.SaveChangesAsync(cancellationToken);

        var body = BuildOrderMessage(order);
        await notificationDispatcher.CreateEmailNotificationAsync(AdminEmail, $"New order #{order.Id}", body, order.Id, cancellationToken);
        await notificationDispatcher.CreateEmailNotificationAsync(visitorEmail, $"Order #{order.Id} received", body, order.Id, cancellationToken);
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
        await notificationDispatcher.CreateEmailNotificationAsync(AdminEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await notificationDispatcher.CreateEmailNotificationAsync(order.VisitorEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        return ToDto(order);
    }

    private string AdminEmail => configuration["NotificationEmails:AdminEmail"] ?? "bmacha2026@gmail.com";

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
