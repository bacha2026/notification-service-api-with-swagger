using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IBulkNotificationCommandPublisher
{
    Task PublishAsync(BulkNotificationRequestedV1 message, CancellationToken cancellationToken);
}
