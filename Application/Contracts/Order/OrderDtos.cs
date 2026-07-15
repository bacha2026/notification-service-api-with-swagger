using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

/// <summary>Request to create an order from a visitor's current cart.</summary>
public sealed record CreateOrderRequest
{
    public CreateOrderRequest()
    {
    }

    public CreateOrderRequest(string visitorEmail)
    {
        VisitorEmail = visitorEmail;
    }

    /// <summary>Email address that identifies the visitor and their cart.</summary>
    [EmailAddress, StringLength(320)]
    public string VisitorEmail { get; init; } = string.Empty;
}

/// <summary>Request to update the lifecycle statuses of an order.</summary>
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

    /// <summary>Overall order status.</summary>
    [Required, EnumDataType(typeof(OrderStatus))]
    public OrderStatus OrderStatus { get; init; }

    /// <summary>Current payment status.</summary>
    [Required, EnumDataType(typeof(PaymentStatus))]
    public PaymentStatus PaymentStatus { get; init; }

    /// <summary>Current fulfillment status.</summary>
    [Required, EnumDataType(typeof(FulfillmentStatus))]
    public FulfillmentStatus FulfillmentStatus { get; init; }

    /// <summary>Current delivery status.</summary>
    [Required, EnumDataType(typeof(DeliveryStatus))]
    public DeliveryStatus DeliveryStatus { get; init; }
}

/// <summary>A product line captured on an order.</summary>
public sealed record OrderItemDto
{
    public OrderItemDto(int productId, string productName, decimal unitPrice, int quantity, decimal subtotal) =>
        (ProductId, ProductName, UnitPrice, Quantity, Subtotal) = (productId, productName, unitPrice, quantity, subtotal);

    /// <summary>Identifier of the ordered product.</summary>
    public int ProductId { get; init; }
    /// <summary>Product name captured when the order was created.</summary>
    public string ProductName { get; init; }
    /// <summary>Price per unit captured when the order was created.</summary>
    public decimal UnitPrice { get; init; }
    /// <summary>Number of units ordered.</summary>
    public int Quantity { get; init; }
    /// <summary>Line total calculated as unit price multiplied by quantity.</summary>
    public decimal Subtotal { get; init; }
}

/// <summary>An order and its product lines, totals, and lifecycle statuses.</summary>
public sealed record OrderDto
{
    public OrderDto(int id, string visitorEmail, OrderStatus orderStatus, PaymentStatus paymentStatus, FulfillmentStatus fulfillmentStatus, DeliveryStatus deliveryStatus, decimal totalAmount, DateTimeOffset createdAtUtc, IReadOnlyCollection<OrderItemDto> items) =>
        (Id, VisitorEmail, OrderStatus, PaymentStatus, FulfillmentStatus, DeliveryStatus, TotalAmount, CreatedAtUtc, Items) = (id, visitorEmail, orderStatus, paymentStatus, fulfillmentStatus, deliveryStatus, totalAmount, createdAtUtc, items);

    /// <summary>Unique identifier of the order.</summary>
    public int Id { get; init; }
    /// <summary>Email address of the visitor who placed the order.</summary>
    public string VisitorEmail { get; init; }
    /// <summary>Overall order status.</summary>
    public OrderStatus OrderStatus { get; init; }
    /// <summary>Current payment status.</summary>
    public PaymentStatus PaymentStatus { get; init; }
    /// <summary>Current fulfillment status.</summary>
    public FulfillmentStatus FulfillmentStatus { get; init; }
    /// <summary>Current delivery status.</summary>
    public DeliveryStatus DeliveryStatus { get; init; }
    /// <summary>Total value of the order.</summary>
    public decimal TotalAmount { get; init; }
    /// <summary>Date and time when the order was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>Product lines included in the order.</summary>
    public IReadOnlyCollection<OrderItemDto> Items { get; init; }
}
