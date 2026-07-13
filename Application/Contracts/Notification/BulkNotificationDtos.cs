using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

public sealed record BulkNotificationItemRequest
{
    public BulkNotificationItemRequest()
    {
    }

    public BulkNotificationItemRequest(string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId)
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

public sealed record CreateBulkNotificationsRequest
{
    public CreateBulkNotificationsRequest()
    {
    }

    public CreateBulkNotificationsRequest(IReadOnlyList<BulkNotificationItemRequest> notifications)
    {
        Notifications = notifications;
    }

    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<BulkNotificationItemRequest> Notifications { get; init; } = Array.Empty<BulkNotificationItemRequest>();
}

public sealed record BulkNotificationJobDto(
    Guid JobId,
    string Status,
    int TotalCount,
    int ProcessedCount,
    int SucceededCount,
    int FailedCount,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Error);
