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

public sealed record CreateNotificationRequest(
    string RecipientEmail,
    NotificationChannel Channel,
    string Subject,
    string Body,
    int? OrderId);

public sealed record UpdateNotificationRequest(
    string RecipientEmail,
    NotificationChannel Channel,
    string Subject,
    string Body,
    int? OrderId,
    bool IsRead);
