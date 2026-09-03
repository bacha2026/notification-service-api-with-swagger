using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;

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

    public async Task<IReadOnlyList<Notification>> GetCompletedNotificationsForRecipientAsync(
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        var normalizedRecipientEmail = recipientEmail.Trim();

        return await dbContext.Notifications
            .Where(notification => notification.RecipientEmail == normalizedRecipientEmail
                && notification.BulkNotificationJobId.HasValue
                && notification.BulkNotificationJobItemId.HasValue
                && dbContext.BulkNotificationJobs.Any(job =>
                    job.Id == notification.BulkNotificationJobId
                    && job.Status == BulkNotificationJobStatuses.Completed)
                && dbContext.BulkNotificationJobItems.Any(item =>
                    item.Id == notification.BulkNotificationJobItemId
                    && item.JobId == notification.BulkNotificationJobId
                    && item.Status == BulkNotificationItemStatuses.Succeeded))
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<Notification?> GetCompletedNotificationForRecipientAsync(
        int id,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        return dbContext.Notifications.SingleOrDefaultAsync(notification =>
            notification.Id == id
            && notification.RecipientEmail == recipientEmail
            && notification.BulkNotificationJobId.HasValue
            && notification.BulkNotificationJobItemId.HasValue
            && dbContext.BulkNotificationJobs.Any(job =>
                job.Id == notification.BulkNotificationJobId
                && job.Status == BulkNotificationJobStatuses.Completed)
            && dbContext.BulkNotificationJobItems.Any(item =>
                item.Id == notification.BulkNotificationJobItemId
                && item.JobId == notification.BulkNotificationJobId
                && item.Status == BulkNotificationItemStatuses.Succeeded),
            cancellationToken);
    }

    public async Task<bool> DeleteCompletedNotificationForRecipientAsync(
        int id,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        var notification = await GetCompletedNotificationForRecipientAsync(
            id,
            recipientEmail.Trim(),
            cancellationToken);
        if (notification is null)
        {
            return false;
        }

        dbContext.Notifications.Remove(notification);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<Notification>> GetNotificationsForVisitorAsync(
        string visitorEmail,
        CancellationToken cancellationToken)
    {
        var orderIds = dbContext.Orders
            .Where(order => order.VisitorEmail == visitorEmail)
            .Select(order => order.Id);

        return await dbContext.Notifications
            .Where(notification => notification.RecipientEmail == visitorEmail
                || notification.OrderId.HasValue && orderIds.Contains(notification.OrderId.Value))
            .ToListAsync(cancellationToken);
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

    public void RemoveRange(IEnumerable<Notification> notifications)
    {
        dbContext.Notifications.RemoveRange(notifications);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

