using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IBulkNotificationJobService
{
    Task<BulkNotificationJobDto> QueueAsync(
        CreateBulkNotificationsRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BulkNotificationJobDto?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken);
}
