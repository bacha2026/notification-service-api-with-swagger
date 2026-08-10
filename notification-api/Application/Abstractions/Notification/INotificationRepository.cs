using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> GetNotificationsAsync(string? recipientEmail, int? orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Notification>> GetNotificationsForVisitorAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<Notification?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> OrderExistsAsync(int orderId, CancellationToken cancellationToken);
    void Add(Notification notification);
    void Remove(Notification notification);
    void RemoveRange(IEnumerable<Notification> notifications);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
