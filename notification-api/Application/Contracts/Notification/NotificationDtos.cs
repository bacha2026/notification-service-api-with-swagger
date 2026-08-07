using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

/// <summary>A notification created for a recipient.</summary>
public sealed record NotificationDto
{
    public NotificationDto(int id, string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId, bool isRead, DateTimeOffset createdAtUtc, DateTimeOffset? sentAtUtc) =>
        (Id, RecipientEmail, Channel, Subject, Body, OrderId, IsRead, CreatedAtUtc, SentAtUtc) = (id, recipientEmail, channel, subject, body, orderId, isRead, createdAtUtc, sentAtUtc);

    /// <summary>Unique identifier of the notification.</summary>
    public int Id { get; init; }
    /// <summary>Email address of the intended recipient.</summary>
    public string RecipientEmail { get; init; }
    /// <summary>Channel used to deliver the notification.</summary>
    public NotificationChannel Channel { get; init; }
    /// <summary>Notification subject or title.</summary>
    public string Subject { get; init; }
    /// <summary>Notification message content.</summary>
    public string Body { get; init; }
    /// <summary>Related order identifier, when applicable.</summary>
    public int? OrderId { get; init; }
    /// <summary>Whether the recipient has read the notification.</summary>
    public bool IsRead { get; init; }
    /// <summary>Date and time when the notification was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>Date and time when delivery succeeded, in UTC; otherwise null.</summary>
    public DateTimeOffset? SentAtUtc { get; init; }
}

/// <summary>Request to create and dispatch a notification.</summary>
public sealed record CreateNotificationRequest
{
    public CreateNotificationRequest()
    {
    }

    public CreateNotificationRequest(string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId)
    {
        RecipientEmail = recipientEmail;
        Channel = channel;
        Subject = subject;
        Body = body;
        OrderId = orderId;
    }

    /// <summary>Email address of the intended recipient.</summary>
    [Required, EmailAddress, StringLength(320, MinimumLength = 1)]
    public string RecipientEmail { get; init; } = string.Empty;

    /// <summary>Channel to use for delivery.</summary>
    [Required, EnumDataType(typeof(NotificationChannel))]
    public NotificationChannel Channel { get; init; }

    /// <summary>Notification subject or title.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    /// <summary>Notification message content.</summary>
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = string.Empty;

    /// <summary>Related order identifier, when applicable.</summary>
    [Range(1, int.MaxValue)]
    public int? OrderId { get; init; }
}

/// <summary>Request to replace a notification's editable details and read state.</summary>
public sealed record UpdateNotificationRequest
{
    public UpdateNotificationRequest()
    {
    }

    public UpdateNotificationRequest(string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId, bool isRead)
    {
        RecipientEmail = recipientEmail;
        Channel = channel;
        Subject = subject;
        Body = body;
        OrderId = orderId;
        IsRead = isRead;
    }

    /// <summary>Email address of the intended recipient.</summary>
    [Required, EmailAddress, StringLength(320, MinimumLength = 1)]
    public string RecipientEmail { get; init; } = string.Empty;

    /// <summary>Channel to use for delivery.</summary>
    [Required, EnumDataType(typeof(NotificationChannel))]
    public NotificationChannel Channel { get; init; }

    /// <summary>Notification subject or title.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    /// <summary>Notification message content.</summary>
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = string.Empty;

    /// <summary>Related order identifier, when applicable.</summary>
    [Range(1, int.MaxValue)]
    public int? OrderId { get; init; }

    /// <summary>Whether the recipient has read the notification.</summary>
    public bool IsRead { get; init; }
}
