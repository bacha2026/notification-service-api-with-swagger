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

    public static Order CreateFromCart(string visitorEmail, IReadOnlyCollection<CartItem> cartItems, DateTimeOffset createdAtUtc)
    {
        if (cartItems.Count == 0)
        {
            throw new InvalidOperationException("The cart is empty.");
        }

        var order = new Order
        {
            VisitorEmail = visitorEmail.Trim(),
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfillmentStatus = FulfillmentStatus.NotStarted,
            DeliveryStatus = DeliveryStatus.WaitingForRider,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        foreach (var cartItem in cartItems)
        {
            if (cartItem.Product is null)
            {
                throw new InvalidOperationException("Cart item product is required to create an order.");
            }

            order.Items.Add(OrderItem.Create(cartItem.Product, cartItem.Quantity));
        }

        order.RecalculateTotal();
        return order;
    }

    public void UpdateStatuses(OrderStatus orderStatus, PaymentStatus paymentStatus, FulfillmentStatus fulfillmentStatus, DeliveryStatus deliveryStatus, DateTimeOffset updatedAtUtc)
    {
        OrderStatus = orderStatus;
        PaymentStatus = paymentStatus;
        FulfillmentStatus = fulfillmentStatus;
        DeliveryStatus = deliveryStatus;
        UpdatedAtUtc = updatedAtUtc;
    }

    private void RecalculateTotal()
    {
        TotalAmount = Items.Sum(item => item.Subtotal);
    }
}
