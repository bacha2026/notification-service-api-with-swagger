namespace NSA.Domain.Entities;

/// <summary>
/// Links a user to a verified subject from an external OpenID Connect or OAuth provider.
/// Provider access and refresh tokens are intentionally not stored here.
/// </summary>
public sealed class UserExternalIdentity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string ProviderSubject { get; private set; } = null!;
    public string? ProviderEmail { get; private set; }
    public DateTimeOffset LinkedAtUtc { get; private set; }
    public User? User { get; private set; }

    public static UserExternalIdentity Create(
        Guid userId,
        string provider,
        string providerSubject,
        string? providerEmail,
        DateTimeOffset linkedAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(provider) || provider.Trim().Length > 64)
        {
            throw new ArgumentException("Provider is required and cannot exceed 64 characters.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(providerSubject) || providerSubject.Trim().Length > 512)
        {
            throw new ArgumentException("ProviderSubject is required and cannot exceed 512 characters.", nameof(providerSubject));
        }

        if (providerEmail?.Trim().Length > 320)
        {
            throw new ArgumentException("ProviderEmail cannot exceed 320 characters.", nameof(providerEmail));
        }

        return new UserExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider.Trim(),
            ProviderSubject = providerSubject.Trim(),
            ProviderEmail = string.IsNullOrWhiteSpace(providerEmail) ? null : providerEmail.Trim(),
            LinkedAtUtc = linkedAtUtc
        };
    }
}

