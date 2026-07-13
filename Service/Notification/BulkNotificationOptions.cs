using System.ComponentModel.DataAnnotations;

namespace NSA.Service;

public sealed class BulkNotificationOptions
{
    public const string SectionName = "BulkNotifications";

    [Range(1, 10_000)]
    public int QueueCapacity { get; init; } = 100;

    [Range(1, 10_000)]
    public int MaxTrackedJobs { get; init; } = 1_000;

    [Range(1, 100)]
    public int MaxBatchSize { get; init; } = 100;

    [Range(1, 1_440)]
    public int CompletedJobRetentionMinutes { get; init; } = 60;
}
