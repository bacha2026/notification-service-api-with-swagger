using NSA.Domain.Enums;

namespace NSA.Domain.Entities;

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
