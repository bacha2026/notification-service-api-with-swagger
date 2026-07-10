using NSA.Application.Abstractions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class NotificationDispatcher(INotificationRepository notificationRepository, IEmailSender emailSender) : INotificationDispatcher
{
    public async Task<Notification> CreateEmailNotificationAsync(string recipientEmail, string subject, string body, int? orderId, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(recipientEmail, NotificationChannel.Email, subject, body, orderId, DateTimeOffset.UtcNow);
        notification.MarkAsSent(DateTimeOffset.UtcNow);

        notificationRepository.Add(notification);
        await emailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
        return notification;
    }
}
