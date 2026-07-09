using NSA.Domain.Enums;

namespace NSA.Domain.Entities;

public sealed class Order
{
    public int Id { get; set; }
    public required string VisitorEmail { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public FulfillmentStatus FulfillmentStatus { get; set; }
    public DeliveryStatus DeliveryStatus { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
