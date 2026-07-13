using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

public sealed record CreateOrderRequest
{
    public CreateOrderRequest()
    {
    }

    public CreateOrderRequest(string visitorEmail)
    {
        VisitorEmail = visitorEmail;
    }

    [EmailAddress, StringLength(320)]
    public string VisitorEmail { get; init; } = string.Empty;
}

public sealed record UpdateOrderStatusRequest
{
    public UpdateOrderStatusRequest()
    {
    }

    public UpdateOrderStatusRequest(
        OrderStatus orderStatus,
        PaymentStatus paymentStatus,
        FulfillmentStatus fulfillmentStatus,
        DeliveryStatus deliveryStatus)
    {
        OrderStatus = orderStatus;
        PaymentStatus = paymentStatus;
        FulfillmentStatus = fulfillmentStatus;
        DeliveryStatus = deliveryStatus;
    }

    [Required, EnumDataType(typeof(OrderStatus))]
    public OrderStatus OrderStatus { get; init; }

    [Required, EnumDataType(typeof(PaymentStatus))]
    public PaymentStatus PaymentStatus { get; init; }

    [Required, EnumDataType(typeof(FulfillmentStatus))]
    public FulfillmentStatus FulfillmentStatus { get; init; }

    [Required, EnumDataType(typeof(DeliveryStatus))]
    public DeliveryStatus DeliveryStatus { get; init; }
}

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
