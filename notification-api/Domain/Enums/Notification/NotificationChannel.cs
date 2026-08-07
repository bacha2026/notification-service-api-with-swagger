namespace NSA.Domain.Enums;

/// <summary>The delivery mechanism used for a notification.</summary>
public enum NotificationChannel
{
    /// <summary>Deliver the notification by email.</summary>
    Email = 1,
    /// <summary>Deliver the notification by SMS text message.</summary>
    Sms = 2,
    /// <summary>Deliver the notification as a push notification.</summary>
    Push = 3,
    /// <summary>Display the notification inside the application.</summary>
    InApp = 4
}
