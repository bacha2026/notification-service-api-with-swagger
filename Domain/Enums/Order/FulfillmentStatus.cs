namespace NSA.Domain.Enums;

/// <summary>The current preparation and fulfillment stage of an order.</summary>
public enum FulfillmentStatus
{
    /// <summary>Fulfillment has not started.</summary>
    NotStarted = 1,
    /// <summary>Order items are being picked.</summary>
    Picking = 2,
    /// <summary>Order items are being packed.</summary>
    Packing = 3,
    /// <summary>All order items have been packed.</summary>
    Packed = 4,
    /// <summary>The order is ready for collection.</summary>
    Ready = 5,
    /// <summary>The prepared order has been assigned to a rider.</summary>
    AssignedToRider = 6
}
