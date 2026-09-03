using System.ComponentModel.DataAnnotations;

namespace NSA.Infrastructure.Configuration;

public sealed class DurabilityOptions
{
    public const string SectionName = "Durability";

    [Range(1, 720)]
    public int IdempotencyRetentionHours { get; init; } = 24;
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    [Range(1, 300)]
    public int PollIntervalSeconds { get; init; } = 2;

    [Range(5, 600)]
    public int LeaseSeconds { get; init; } = 30;

    [Range(1, 3600)]
    public int MaximumBackoffSeconds { get; init; } = 300;
}

