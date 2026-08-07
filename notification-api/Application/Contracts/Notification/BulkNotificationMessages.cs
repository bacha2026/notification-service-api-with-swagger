namespace NSA.Application.Contracts;

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
