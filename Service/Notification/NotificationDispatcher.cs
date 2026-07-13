using NSA.Application.Abstractions;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class NotificationDispatcher(INotificationRepository notificationRepository, IEmailSender emailSender) : INotificationDispatcher
{
    public async Task<Notification> CreateEmailNotificationAsync(string recipientEmail, string subject, string body, int? orderId, CancellationToken cancellationToken)
    {
        if (orderId is not null && !await notificationRepository.OrderExistsAsync(orderId.Value, cancellationToken))
        {
            throw new RequestValidationException($"Order {orderId.Value} does not exist.");
        }

        var notification = Notification.Create(recipientEmail, NotificationChannel.Email, subject, body, orderId, DateTimeOffset.UtcNow);
        notificationRepository.Add(notification);
        await notificationRepository.SaveChangesAsync(cancellationToken);

        var deliveryOutcome = await emailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
        if (deliveryOutcome == EmailDeliveryOutcome.AcceptedByProvider)
        {
            notification.MarkAsSent(DateTimeOffset.UtcNow);
            await notificationRepository.SaveChangesAsync(cancellationToken);
        }

        return notification;
    }
}
