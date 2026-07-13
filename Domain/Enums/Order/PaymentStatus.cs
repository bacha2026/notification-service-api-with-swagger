namespace NSA.Domain.Enums;

public enum PaymentStatus
{
    Unpaid = 1,
    Pending = 2,
    Processing = 3,
    Paid = 4,
    PartiallyPaid = 5,
    Failed = 6,
    Refunded = 7,
    PartiallyRefunded = 8,
    Chargeback = 9
}
