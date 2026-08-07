namespace NSA.Domain.Enums;

/// <summary>The overall lifecycle state of an order.</summary>
public enum OrderStatus
{
    /// <summary>The order is still a draft.</summary>
    Draft = 1,
    /// <summary>The order has been submitted and is pending review.</summary>
    Pending = 2,
    /// <summary>The order is waiting for payment.</summary>
    AwaitingPayment = 3,
    /// <summary>The order payment has been verified.</summary>
    PaymentVerified = 4,
    /// <summary>The order has been confirmed.</summary>
    Confirmed = 5,
    /// <summary>The order is being prepared.</summary>
    Preparing = 6,
    /// <summary>The order is ready for pickup.</summary>
    ReadyForPickup = 7,
    /// <summary>The order is out for delivery.</summary>
    OutForDelivery = 8,
    /// <summary>The order has been delivered.</summary>
    Delivered = 9,
    /// <summary>The order lifecycle is complete.</summary>
    Completed = 10,
    /// <summary>The order was cancelled.</summary>
    Cancelled = 11,
    /// <summary>The order was rejected.</summary>
    Rejected = 12,
    /// <summary>The order payment was refunded.</summary>
    Refunded = 13
}
