using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

public sealed record NotificationDto(
    int Id,
    string RecipientEmail,
    NotificationChannel Channel,
    string Subject,
    string Body,
    int? OrderId,
    bool IsRead,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc);

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

    [Required, EmailAddress, StringLength(320, MinimumLength = 1)]
    public string RecipientEmail { get; init; } = string.Empty;

    [Required, EnumDataType(typeof(NotificationChannel))]
    public NotificationChannel Channel { get; init; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? OrderId { get; init; }
}

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

    [Required, EmailAddress, StringLength(320, MinimumLength = 1)]
    public string RecipientEmail { get; init; } = string.Empty;

    [Required, EnumDataType(typeof(NotificationChannel))]
    public NotificationChannel Channel { get; init; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? OrderId { get; init; }

    public bool IsRead { get; init; }
}
