namespace NSA.Domain.Enums;

/// <summary>The current delivery stage of an order.</summary>
public enum DeliveryStatus
{
    /// <summary>The order is waiting for a delivery rider.</summary>
    WaitingForRider = 1,
    /// <summary>A rider has been assigned to the order.</summary>
    RiderAssigned = 2,
    /// <summary>The rider is travelling to the store.</summary>
    RiderEnRouteToStore = 3,
    /// <summary>The rider has arrived at the store.</summary>
    RiderArrivedAtStore = 4,
    /// <summary>The rider has collected the order.</summary>
    PickedUp = 5,
    /// <summary>The order is travelling to its destination.</summary>
    OnTheWay = 6,
    /// <summary>The rider is near the delivery destination.</summary>
    NearDestination = 7,
    /// <summary>The order was delivered successfully.</summary>
    Delivered = 8,
    /// <summary>The delivery attempt failed.</summary>
    DeliveryFailed = 9,
    /// <summary>The order was returned after an unsuccessful delivery.</summary>
    Returned = 10
}
