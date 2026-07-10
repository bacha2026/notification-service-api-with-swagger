using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string? recipientEmail, int? orderId, CancellationToken cancellationToken);
    Task<NotificationDto?> GetNotificationAsync(int id, CancellationToken cancellationToken);
    Task<NotificationDto> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken);
    Task<NotificationDto?> UpdateNotificationAsync(int id, UpdateNotificationRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteNotificationAsync(int id, CancellationToken cancellationToken);
}
