using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;

namespace NSA.Service;

public sealed class OrderService(
    IOrderRepository orderRepository,
    INotificationService notificationService,
    IBulkNotificationJobService bulkNotificationJobs,
    NotificationDbContext dbContext,
    IdempotencyStore idempotencyStore,
    NotificationRecipientSettings recipientSettings,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider) : IOrderService
{
    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        var email = ResolveVisitorEmail(visitorEmail);
        var orders = await orderRepository.GetOrdersForVisitorAsync(email, cancellationToken);

        return orders.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        EnsureAuthenticatedUserId(userId);
        var orders = await orderRepository.GetOrdersForUserAsync(userId, cancellationToken);

        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderDto?> GetOrderAsync(int id, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, cancellationToken);
        return order is null ? null : ToDto(order);
    }

    public async Task<OrderDto?> GetOrderForUserAsync(int id, Guid userId, CancellationToken cancellationToken)
    {
        EnsureAuthenticatedUserId(userId);
        var order = await orderRepository.GetByIdForUserWithItemsAsync(id, userId, cancellationToken);
        return order is null ? null : ToDto(order);
    }

    public Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
        => CreateOrderAsync(request, idempotency: null, cancellationToken);

    public Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderRequest request,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        var visitorEmail = ResolveVisitorEmail(request.VisitorEmail);
        return CreateOrderAsync(visitorEmail, userId: null, idempotency, cancellationToken);
    }

    public Task<CreateOrderResult> CreateOrderForUserAsync(
        Guid userId,
        string visitorEmail,
        CancellationToken cancellationToken) =>
        CreateOrderForUserAsync(userId, visitorEmail, idempotency: null, cancellationToken);

    public Task<CreateOrderResult> CreateOrderForUserAsync(
        Guid userId,
        string visitorEmail,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticatedUserId(userId);
        return CreateOrderAsync(
            NormalizeRequiredVisitorEmail(visitorEmail),
            userId,
            idempotency,
            cancellationToken);
    }

    private async Task<CreateOrderResult> CreateOrderAsync(
        string visitorEmail,
        Guid? userId,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        var requestHash = IdempotencyRequestHasher.Hash(
            new OrderAdmissionRequest(visitorEmail.ToUpperInvariant()));
        await using var transaction = await idempotencyStore.BeginSerializableTransactionAsync(cancellationToken);
        await idempotencyStore.PurgeExpiredAsync(idempotency, cancellationToken);
        await idempotencyStore.AcquireLockAsync(idempotency, cancellationToken);
        var replay = await idempotencyStore.TryReplayAsync<CreateOrderResult>(
            idempotency,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return replay.Value with { IdempotencyReplayed = true };
        }

        var cartItems = await orderRepository.GetCartItemsForOrderAsync(visitorEmail, cancellationToken);
        if (cartItems.Count == 0)
        {
            throw new RequestValidationException("The cart is empty.");
        }

        var order = Order.CreateFromCart(visitorEmail, cartItems, timeProvider.GetUtcNow(), userId);
        orderRepository.Add(order);
        orderRepository.RemoveCartItems(cartItems);
        await orderRepository.SaveChangesAsync(cancellationToken);

        var job = await bulkNotificationJobs.QueueAsync(
            CreateOrderPlacedNotificationRequest(order),
            correlationIdProvider.CurrentCorrelationId ?? $"order-{order.Id}",
            cancellationToken);
        var result = new CreateOrderResult(
            ToDto(order),
            job,
            NotificationHandoffStatus.Confirmed);
        idempotencyStore.Add(
            idempotency,
            requestHash,
            StatusCodes.Status201Created,
            result,
            nameof(Order),
            order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<OrderDto?> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdWithItemsAsync(id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.UpdateStatuses(
            request.OrderStatus,
            request.PaymentStatus,
            request.FulfillmentStatus,
            request.DeliveryStatus,
            timeProvider.GetUtcNow());

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

        if (!order.CancelByVisitor(timeProvider.GetUtcNow()))
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

    private string AdminEmail => recipientSettings.AdminEmail;

    private CreateBulkNotificationsRequest CreateOrderPlacedNotificationRequest(Order order)
    {
        var body = BuildOrderMessage(order);
        return new CreateBulkNotificationsRequest(
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

    private string ResolveVisitorEmail(string visitorEmail) =>
        recipientSettings.ResolveVisitorEmail(visitorEmail);

    private static string NormalizeRequiredVisitorEmail(string visitorEmail)
    {
        if (string.IsNullOrWhiteSpace(visitorEmail))
        {
            throw new RequestValidationException("Visitor email is required.");
        }

        return visitorEmail.Trim();
    }

    private static void EnsureAuthenticatedUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new RequestValidationException("An authenticated user identifier is required.");
        }
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

    private sealed record OrderAdmissionRequest(string VisitorEmail);
}

