using NSA.Domain.Entities;
using NSA.Domain.Enums;

namespace NSA.Tests;

public sealed class DomainEnumValidationTests
{
    [Fact]
    public void Notification_creation_rejects_an_undefined_channel()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Notification.Create(
            "person@example.com",
            (NotificationChannel)999,
            "Subject",
            "Body",
            null,
            DateTimeOffset.UtcNow));

        Assert.Equal("channel", exception.ParamName);
    }

    [Fact]
    public void Notification_update_rejects_an_undefined_channel_without_mutating_state()
    {
        var notification = Notification.Create(
            "before@example.com",
            NotificationChannel.Email,
            "Before",
            "Before body",
            null,
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => notification.Update(
            "after@example.com",
            (NotificationChannel)999,
            "After",
            "After body",
            42,
            true));

        Assert.Equal("channel", exception.ParamName);
        Assert.Equal("before@example.com", notification.RecipientEmail);
        Assert.Equal(NotificationChannel.Email, notification.Channel);
        Assert.Equal("Before", notification.Subject);
        Assert.Null(notification.OrderId);
        Assert.False(notification.IsRead);
    }

    [Theory]
    [InlineData("orderStatus")]
    [InlineData("paymentStatus")]
    [InlineData("fulfillmentStatus")]
    [InlineData("deliveryStatus")]
    public void Order_update_rejects_each_undefined_status_without_mutating_state(string invalidParameter)
    {
        var originalTimestamp = new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);
        var order = new Order
        {
            VisitorEmail = "person@example.com",
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfillmentStatus = FulfillmentStatus.NotStarted,
            DeliveryStatus = DeliveryStatus.WaitingForRider,
            CreatedAtUtc = originalTimestamp,
            UpdatedAtUtc = originalTimestamp
        };
        var newTimestamp = originalTimestamp.AddHours(1);

        Action update = invalidParameter switch
        {
            "orderStatus" => () => order.UpdateStatuses(
                (OrderStatus)999,
                PaymentStatus.Paid,
                FulfillmentStatus.Packed,
                DeliveryStatus.Delivered,
                newTimestamp),
            "paymentStatus" => () => order.UpdateStatuses(
                OrderStatus.Completed,
                (PaymentStatus)999,
                FulfillmentStatus.Packed,
                DeliveryStatus.Delivered,
                newTimestamp),
            "fulfillmentStatus" => () => order.UpdateStatuses(
                OrderStatus.Completed,
                PaymentStatus.Paid,
                (FulfillmentStatus)999,
                DeliveryStatus.Delivered,
                newTimestamp),
            "deliveryStatus" => () => order.UpdateStatuses(
                OrderStatus.Completed,
                PaymentStatus.Paid,
                FulfillmentStatus.Packed,
                (DeliveryStatus)999,
                newTimestamp),
            _ => throw new InvalidOperationException($"Unknown test parameter {invalidParameter}.")
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(update);

        Assert.Equal(invalidParameter, exception.ParamName);
        Assert.Equal(OrderStatus.Pending, order.OrderStatus);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(FulfillmentStatus.NotStarted, order.FulfillmentStatus);
        Assert.Equal(DeliveryStatus.WaitingForRider, order.DeliveryStatus);
        Assert.Equal(originalTimestamp, order.UpdatedAtUtc);
    }

    [Fact]
    public void Order_update_accepts_defined_statuses()
    {
        var order = new Order
        {
            VisitorEmail = "person@example.com",
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfillmentStatus = FulfillmentStatus.NotStarted,
            DeliveryStatus = DeliveryStatus.WaitingForRider,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var updatedAt = order.UpdatedAtUtc.AddMinutes(1);

        order.UpdateStatuses(
            OrderStatus.Completed,
            PaymentStatus.Paid,
            FulfillmentStatus.Packed,
            DeliveryStatus.Delivered,
            updatedAt);

        Assert.Equal(OrderStatus.Completed, order.OrderStatus);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(FulfillmentStatus.Packed, order.FulfillmentStatus);
        Assert.Equal(DeliveryStatus.Delivered, order.DeliveryStatus);
        Assert.Equal(updatedAt, order.UpdatedAtUtc);
    }
}
