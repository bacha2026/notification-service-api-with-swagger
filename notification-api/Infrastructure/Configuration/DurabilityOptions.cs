using System.ComponentModel.DataAnnotations;

namespace NSA.Infrastructure.Configuration;

public sealed class DurabilityOptions
{
    public const string SectionName = "Durability";

    [Range(1, 720)]
    public int IdempotencyRetentionHours { get; init; } = 24;
}
