using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IBulkNotificationJobService
{
    Task<BulkNotificationJobDto> QueueAsync(
        CreateBulkNotificationsRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IdempotentResult<BulkNotificationJobDto>> QueueAsync(
        CreateBulkNotificationsRequest request,
        string correlationId,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken);

    Task<BulkNotificationJobDto?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken);
}
