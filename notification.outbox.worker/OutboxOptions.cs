using System.ComponentModel.DataAnnotations;

namespace NSA.Outbox.Worker;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(1, 500)]
    public int BatchSize { get; init; } = 50; //determines how many pending outbox messages should be claimed and processed in one run

    [Range(1, 300)]
    public int PollIntervalSeconds { get; init; } = 7;//controls how often the worker checks for due outbox messages while idle or when retry logic is not active

    [Range(5, 600)]
    public int LeaseSeconds { get; init; } = 30;//defines how long a worker can hold a claim on a message before another worker can take it over

    [Range(1, 3600)]
    public int MaximumBackoffSeconds { get; init; } = 300;//limits how long a failed message should wait before retrying again
}

