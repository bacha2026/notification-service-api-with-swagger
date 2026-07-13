using System.ComponentModel.DataAnnotations;

namespace NSA.Infrastructure.Email;

public sealed class PostboundOptions
{
    public const string SectionName = "Postbound";

    [Required, Url]
    public string BaseUrl { get; init; } = "https://api.postbound.com/";

    public string? ApiKey { get; init; }
    public bool Enabled { get; init; }

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; init; } = 10;

    [Range(0, 10)]
    public int RetryCount { get; init; } = 3;

    [Range(1, 60_000)]
    public int InitialRetryDelayMilliseconds { get; init; } = 200;

    [Range(1, 100)]
    public int CircuitBreakerFailures { get; init; } = 3;

    [Range(1, 3_600)]
    public int CircuitBreakDurationSeconds { get; init; } = 30;
}
