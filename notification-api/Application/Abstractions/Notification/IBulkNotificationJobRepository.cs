using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

public interface IBulkNotificationJobRepository
{
    Task<int> CountActiveAsync(CancellationToken cancellationToken);
    Task<BulkNotificationJob?> GetByIdAsync(Guid jobId, CancellationToken cancellationToken);
    Task<BulkNotificationJob?> GetWithItemsAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BulkNotificationJob>> GetCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
    void Add(BulkNotificationJob job);
    void RemoveRange(IEnumerable<BulkNotificationJob> jobs);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
