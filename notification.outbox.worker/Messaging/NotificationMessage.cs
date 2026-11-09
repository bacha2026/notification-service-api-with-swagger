namespace NSA.Outbox.Worker;

/// <summary>
/// Version 1 command published when a persisted bulk notification job is ready.
/// The message intentionally contains no recipient, subject, or body data.
/// </summary>
public sealed record BulkNotificationRequestedV1(
    int SchemaVersion,
    Guid MessageId,
    Guid JobId,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
    public const string MessageType = "nsa.notifications.bulk-requested.v1";
}


public enum NotificationFailureKind { Permanent, Timeout, Backoff, Unknown }

/// <summary>A payload or business state that cannot succeed without correction.</summary>
public class PermanentMessageException(string message) : Exception(message);

public sealed class BulkNotificationJobNotFoundException(Guid jobId)
    : PermanentMessageException($"Bulk notification job {jobId} does not exist.");
