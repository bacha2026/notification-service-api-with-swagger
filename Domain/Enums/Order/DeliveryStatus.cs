namespace NSA.Domain.Enums;

public enum DeliveryStatus
{
    WaitingForRider = 1,
    RiderAssigned = 2,
    RiderEnRouteToStore = 3,
    RiderArrivedAtStore = 4,
    PickedUp = 5,
    OnTheWay = 6,
    NearDestination = 7,
    Delivered = 8,
    DeliveryFailed = 9,
    Returned = 10
}
