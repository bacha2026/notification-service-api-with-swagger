using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

public sealed record CreateOrderRequest(string VisitorEmail);

public sealed record UpdateOrderStatusRequest(
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    DeliveryStatus DeliveryStatus);

public sealed record OrderItemDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Subtotal);

public sealed record OrderDto(
    int Id,
    string VisitorEmail,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    DeliveryStatus DeliveryStatus,
    decimal TotalAmount,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<OrderItemDto> Items);
