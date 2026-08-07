using Microsoft.EntityFrameworkCore;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Persistence.Concrete;

public sealed class NotificationRepository(NotificationDbContext dbContext) : INotificationRepository
{
    public async Task<IReadOnlyList<Notification>> GetNotificationsAsync(string? recipientEmail, int? orderId, CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.AsQueryable();
        if (!string.IsNullOrWhiteSpace(recipientEmail))
        {
            query = query.Where(notification => notification.RecipientEmail == recipientEmail.Trim());
        }

        if (orderId.HasValue)
        {
            query = query.Where(notification => notification.OrderId == orderId.Value);
        }

        return await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<Notification?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return dbContext.Notifications.FindAsync([id], cancellationToken).AsTask();
    }

    public Task<bool> OrderExistsAsync(int orderId, CancellationToken cancellationToken)
    {
        return dbContext.Orders.AnyAsync(order => order.Id == orderId, cancellationToken);
    }

    public void Add(Notification notification)
    {
        dbContext.Notifications.Add(notification);
    }

    public void Remove(Notification notification)
    {
        dbContext.Notifications.Remove(notification);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
