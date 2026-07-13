using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IBulkNotificationJobService
{
    BulkNotificationJobDto Queue(CreateBulkNotificationsRequest request);
    BulkNotificationJobDto? GetStatus(Guid jobId);
}
