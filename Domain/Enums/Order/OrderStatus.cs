namespace NSA.Domain.Enums;

public enum OrderStatus
{
    Draft = 1,
    Pending = 2,
    AwaitingPayment = 3,
    PaymentVerified = 4,
    Confirmed = 5,
    Preparing = 6,
    ReadyForPickup = 7,
    OutForDelivery = 8,
    Delivered = 9,
    Completed = 10,
    Cancelled = 11,
    Rejected = 12,
    Refunded = 13
}
