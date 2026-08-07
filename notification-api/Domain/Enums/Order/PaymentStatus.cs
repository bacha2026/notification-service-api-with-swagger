namespace NSA.Domain.Enums;

/// <summary>The current payment state of an order.</summary>
public enum PaymentStatus
{
    /// <summary>No payment has been received.</summary>
    Unpaid = 1,
    /// <summary>A payment is pending.</summary>
    Pending = 2,
    /// <summary>A payment is being processed.</summary>
    Processing = 3,
    /// <summary>The order has been paid in full.</summary>
    Paid = 4,
    /// <summary>Part of the amount due has been paid.</summary>
    PartiallyPaid = 5,
    /// <summary>The payment attempt failed.</summary>
    Failed = 6,
    /// <summary>The payment was refunded in full.</summary>
    Refunded = 7,
    /// <summary>Part of the payment was refunded.</summary>
    PartiallyRefunded = 8,
    /// <summary>The payment is subject to a chargeback.</summary>
    Chargeback = 9
}
