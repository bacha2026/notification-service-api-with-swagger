using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class NotificationService(INotificationRepository notificationRepository) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string? recipientEmail, int? orderId, CancellationToken cancellationToken)
    {
        var notifications = await notificationRepository.GetNotificationsAsync(recipientEmail, orderId, cancellationToken);
        return notifications.Select(ToDto).ToList();
    }

    public async Task<NotificationDto?> GetNotificationAsync(int id, CancellationToken cancellationToken)
    {
        var notification = await notificationRepository.GetByIdAsync(id, cancellationToken);
        return notification is null ? null : ToDto(notification);
    }

    public async Task<NotificationDto> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        await ValidateOrderAsync(request.OrderId, cancellationToken);
        var notification = Notification.Create(request.RecipientEmail, request.Channel, request.Subject, request.Body, request.OrderId, DateTimeOffset.UtcNow);

        notificationRepository.Add(notification);
        await notificationRepository.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    public async Task<NotificationDto?> UpdateNotificationAsync(int id, UpdateNotificationRequest request, CancellationToken cancellationToken)
    {
        var notification = await notificationRepository.GetByIdAsync(id, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        await ValidateOrderAsync(request.OrderId, cancellationToken);
        notification.Update(request.RecipientEmail, request.Channel, request.Subject, request.Body, request.OrderId, request.IsRead);

        await notificationRepository.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    public async Task<bool> DeleteNotificationAsync(int id, CancellationToken cancellationToken)
    {
        var notification = await notificationRepository.GetByIdAsync(id, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notificationRepository.Remove(notification);
        await notificationRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static NotificationDto ToDto(Notification notification)
    {
        return new NotificationDto(notification.Id, notification.RecipientEmail, notification.Channel, notification.Subject, notification.Body, notification.OrderId, notification.IsRead, notification.CreatedAtUtc, notification.SentAtUtc);
    }

    private async Task ValidateOrderAsync(int? orderId, CancellationToken cancellationToken)
    {
        if (orderId is not null && !await notificationRepository.OrderExistsAsync(orderId.Value, cancellationToken))
        {
            throw new RequestValidationException($"Order {orderId.Value} does not exist.");
        }
    }
}
