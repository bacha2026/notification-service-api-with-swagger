using System.ComponentModel.DataAnnotations;

namespace NSA.Infrastructure.Configuration;

/// <summary>Host-bound security settings for local account lockout behavior.</summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    [Range(1, 20)]
    public int MaxFailedAccessAttempts { get; init; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; init; } = 15;
}

