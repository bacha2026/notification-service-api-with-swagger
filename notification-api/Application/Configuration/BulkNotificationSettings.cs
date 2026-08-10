namespace NSA.Application.Configuration;

/// <summary>Validated bulk-notification limits consumed by the application use case.</summary>
public sealed record BulkNotificationSettings(
    int MaxTrackedJobs,
    int MaxBatchSize,
    int CompletedJobRetentionMinutes);
