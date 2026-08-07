using System.Diagnostics;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class OrderService(
    IOrderRepository orderRepository,
    INotificationService notificationService,
    IBulkNotificationJobService bulkNotificationJobs,
    IHostApplicationLifetime applicationLifetime,
    IConfiguration configuration,
    ILogger<OrderService> logger) : IOrderService
{
    private static readonly TimeSpan PostCommitHandoffTimeout = TimeSpan.FromSeconds(15);

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

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
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

        var handoff = await QueueOrderPlacedNotificationsSafelyAsync(order);
        return new CreateOrderResult(ToDto(order), handoff.Job, handoff.Status);
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
        await CreateNotificationAsync(AdminEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await CreateNotificationAsync(order.VisitorEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        return ToDto(order);
    }

    public async Task<OrderDto?> CancelOrderAsync(
        int id,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var visitorEmail = NormalizeRequiredVisitorEmail(request.VisitorEmail);
        var order = await orderRepository.GetByIdWithItemsAsync(id, cancellationToken);
        if (order is null
            || !string.Equals(order.VisitorEmail, visitorEmail, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!order.CancelByVisitor(DateTimeOffset.UtcNow))
        {
            return ToDto(order);
        }

        var body = $"Order #{order.Id} for {order.VisitorEmail} was cancelled by the visitor. "
            + BuildOrderMessage(order);
        await CreateNotificationAsync(
            AdminEmail,
            $"Order #{order.Id} cancelled by visitor",
            body,
            order.Id,
            cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        return ToDto(order);
    }

    private string AdminEmail => configuration["NotificationEmails:AdminEmail"] ?? "admin@example.test";

    private async Task<(BulkNotificationJobDto? Job, NotificationHandoffStatus Status)>
        QueueOrderPlacedNotificationsSafelyAsync(Order order)
    {
        var body = BuildOrderMessage(order);
        var request = new CreateBulkNotificationsRequest(
        [
            new BulkNotificationItemRequest(
                AdminEmail,
                NotificationChannel.InApp,
                $"New order #{order.Id}",
                body,
                order.Id),
            new BulkNotificationItemRequest(
                order.VisitorEmail,
                NotificationChannel.InApp,
                $"Order #{order.Id} received",
                body,
                order.Id)
        ]);

        try
        {
            using var handoffTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                applicationLifetime.ApplicationStopping);
            handoffTimeout.CancelAfter(PostCommitHandoffTimeout);

            var job = await bulkNotificationJobs.QueueAsync(
                request,
                Activity.Current?.Id ?? $"order-{order.Id}",
                handoffTimeout.Token);
            return (job, NotificationHandoffStatus.Confirmed);
        }
        catch (BulkNotificationPublishException exception)
        {
            logger.LogWarning(
                exception,
                "Order {OrderId} was saved, but RabbitMQ handoff for notification job {JobId} could not be confirmed",
                order.Id,
                exception.Job.JobId);
            return (exception.Job, NotificationHandoffStatus.Unconfirmed);
        }
        catch (BulkNotificationCapacityException exception)
        {
            logger.LogWarning(
                exception,
                "Order {OrderId} was saved, but notification job admission was rejected at capacity",
                order.Id);
            return (null, NotificationHandoffStatus.Rejected);
        }
        catch (OperationCanceledException exception)
            when (!applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Order {OrderId} was saved, but notification job handoff exceeded the {TimeoutSeconds}-second limit",
                order.Id,
                PostCommitHandoffTimeout.TotalSeconds);
            return (null, NotificationHandoffStatus.Rejected);
        }
    }

    private Task<NotificationDto> CreateNotificationAsync(
        string recipientEmail,
        string subject,
        string body,
        int orderId,
        CancellationToken cancellationToken)
        => notificationService.CreateNotificationAsync(
            new CreateNotificationRequest(
                recipientEmail,
                NotificationChannel.InApp,
                subject,
                body,
                orderId),
            cancellationToken);

    private string ResolveVisitorEmail(string visitorEmail)
    {
        return string.IsNullOrWhiteSpace(visitorEmail)
            ? configuration["NotificationEmails:DefaultVisitorEmail"] ?? "visitor@example.test"
            : visitorEmail.Trim();
    }

    private static string NormalizeRequiredVisitorEmail(string visitorEmail)
    {
        if (string.IsNullOrWhiteSpace(visitorEmail))
        {
            throw new RequestValidationException("Visitor email is required.");
        }

        return visitorEmail.Trim();
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
