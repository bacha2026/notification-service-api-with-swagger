using System.ComponentModel.DataAnnotations;

namespace NSA.Infrastructure.Configuration;

/// <summary>Host-bound settings used to issue and validate bearer JWTs.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    [Required, StringLength(200, MinimumLength = 1)]
    public string Issuer { get; init; } = "NSA.NotificationService";

    [Required, StringLength(200, MinimumLength = 1)]
    public string Audience { get; init; } = "NSA.NotificationClient";

    /// <summary>
    /// Symmetric signing key supplied only through a secret configuration provider,
    /// such as an environment variable or development user-secrets.
    /// </summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(5, 60)]
    public int AccessTokenLifetimeMinutes { get; init; } = 15;
}

