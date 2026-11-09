
namespace NSA.Outbox.Worker;

public static class BulkNotificationJobStatuses
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Retrying = "Retrying";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string PublishFailed = "PublishFailed";
    public const string DeadLettered = "DeadLettered";

    public static readonly string[] Terminal =
    [
        Completed,
        CompletedWithErrors,
        PublishFailed,
        DeadLettered
    ];
}

public static class BulkNotificationItemStatuses
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public sealed class BulkNotificationJob
{
    public Guid Id { get; set; }
    public required string Status { get; set; }
    public int MessageSchemaVersion { get; set; }
    public required string CorrelationId { get; set; }
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public DateTimeOffset QueuedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
    public ICollection<BulkNotificationJobItem> Items { get; set; } = new List<BulkNotificationJobItem>();
}

public sealed class BulkNotificationJobItem
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public BulkNotificationJob? Job { get; set; }
    public int Sequence { get; set; }
    public required string RecipientEmail { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public int? OrderId { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}


/// <summary>The delivery mechanism used for a notification.</summary>
public enum NotificationChannel
{
    /// <summary>Deliver the notification by email.</summary>
    Email = 1,
    /// <summary>Deliver the notification by SMS text message.</summary>
    Sms = 2,
    /// <summary>Deliver the notification as a push notification.</summary>
    Push = 3,
    /// <summary>Display the notification inside the application.</summary>
    InApp = 4
}

// Only the order key is needed to validate notification references.
public sealed class OrderReference
{
    public int Id { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string AggregateType { get; set; }
    public required string AggregateId { get; set; }
    public required string MessageType { get; set; }
    public int SchemaVersion { get; set; }
    public required string Payload { get; set; }
    public required string Exchange { get; set; }
    public required string RoutingKey { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public string? LastError { get; set; }
}
