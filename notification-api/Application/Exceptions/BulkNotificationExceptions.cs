using NSA.Application.Contracts;

namespace NSA.Application.Exceptions;

/// <summary>The persisted-job admission limit has been reached, so no new job was created.</summary>
public sealed class BulkNotificationCapacityException(string message)
    : ServiceUnavailableException(message);

/// <summary>A job was persisted, but RabbitMQ publication could not be confirmed.</summary>
public sealed class BulkNotificationPublishException(
    string message,
    BulkNotificationJobDto job)
    : ServiceUnavailableException(message)
{
    public BulkNotificationJobDto Job { get; } = job;
}
