using NSA.Domain.Entities;

namespace NSA.Persistence.Interfaces;

public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> GetNotificationsAsync(string? recipientEmail, int? orderId, CancellationToken cancellationToken);
    Task<Notification?> GetByIdAsync(int id, CancellationToken cancellationToken);
    void Add(Notification notification);
    void Remove(Notification notification);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
