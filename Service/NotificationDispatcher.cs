using NSA.Application.Abstractions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;

namespace NSA.Service;

public sealed class NotificationDispatcher(NotificationDbContext dbContext, IEmailSender emailSender) : INotificationDispatcher
{
    public async Task<Notification> CreateEmailNotificationAsync(string recipientEmail, string subject, string body, int? orderId, CancellationToken cancellationToken)
    {
        var notification = new Notification
        {
            RecipientEmail = recipientEmail,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            OrderId = orderId,
            IsRead = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SentAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.Notifications.Add(notification);
        await emailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
        return notification;
    }
}
