namespace NSA.Application.Exceptions;

/// <summary>The persisted-job admission limit has been reached, so no new job was created.</summary>
public sealed class BulkNotificationCapacityException(string message)
    : ServiceUnavailableException(message);

