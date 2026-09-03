using NSA.Domain.Enums;

namespace NSA.Domain.Entities;

/// <summary>
/// Represents an enrolled MFA method. Authenticator secrets must be protected by
/// a dedicated data-protection adapter before an enrollment flow is enabled.
/// </summary>
public sealed class UserMfaMethod
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public MfaMethodType Type { get; private set; }
    public string? Destination { get; private set; }
    public string? ProtectedSecret { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }
    public User? User { get; private set; }

    /// <summary>Creates an unverified enrollment that must be enabled only after verification.</summary>
    public static UserMfaMethod Create(
        Guid userId,
        MfaMethodType type,
        string? destination,
        string? protectedSecret,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "MFA method type is not defined.");
        }

        if (destination?.Trim().Length > 320 || protectedSecret?.Length > 2048)
        {
            throw new ArgumentException("MFA destination or protected secret exceeds its maximum length.");
        }

        if (type == MfaMethodType.AuthenticatorApp && string.IsNullOrWhiteSpace(protectedSecret))
        {
            throw new ArgumentException("Authenticator app enrollment requires a protected secret.", nameof(protectedSecret));
        }

        if (type is MfaMethodType.Email or MfaMethodType.Sms && string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Email and SMS MFA enrollment require a destination.", nameof(destination));
        }

        return new UserMfaMethod
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Destination = string.IsNullOrWhiteSpace(destination) ? null : destination.Trim(),
            ProtectedSecret = string.IsNullOrWhiteSpace(protectedSecret) ? null : protectedSecret,
            IsEnabled = false,
            CreatedAtUtc = now
        };
    }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void MarkUsed(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("An MFA method must be enabled before it can be used.");
        }

        LastUsedAtUtc = now;
    }
}

