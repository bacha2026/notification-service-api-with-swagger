using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;

namespace NSA.Persistence.Concrete;

/// <summary>EF Core adapter for bulk-notification job persistence.</summary>
public sealed class BulkNotificationJobRepository(NotificationDbContext dbContext) : IBulkNotificationJobRepository
{
    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        dbContext.BulkNotificationJobs.CountAsync(
            job => !BulkNotificationJobStatuses.Terminal.Contains(job.Status),
            cancellationToken);

    public Task<BulkNotificationJob?> GetByIdAsync(Guid jobId, CancellationToken cancellationToken) =>
        dbContext.BulkNotificationJobs.SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);

    public Task<BulkNotificationJob?> GetWithItemsAsync(Guid jobId, CancellationToken cancellationToken) =>
        dbContext.BulkNotificationJobs
            .Include(job => job.Items)
            .SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);

    public async Task<IReadOnlyList<BulkNotificationJob>> GetCompletedBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken) =>
        await dbContext.BulkNotificationJobs
            .Where(job => job.CompletedAtUtc != null && job.CompletedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

    public void Add(BulkNotificationJob job) => dbContext.BulkNotificationJobs.Add(job);

    public void RemoveRange(IEnumerable<BulkNotificationJob> jobs) => dbContext.BulkNotificationJobs.RemoveRange(jobs);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
