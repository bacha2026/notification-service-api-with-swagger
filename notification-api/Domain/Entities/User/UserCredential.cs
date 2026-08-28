namespace NSA.Domain.Entities;

/// <summary>
/// Stores the local sign-in identity for a user. Passwords are never persisted;
/// <see cref="PasswordHash"/> is produced by the configured password-hashing adapter.
/// </summary>
public sealed class UserCredential
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string? PasswordHash { get; private set; }
    public DateTimeOffset? EmailConfirmedAtUtc { get; private set; }
    public DateTimeOffset? PasswordChangedAtUtc { get; private set; }
    public int FailedAccessCount { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public User? User { get; private set; }

    public static UserCredential Create(Guid userId, string email, string passwordHash, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 320)
        {
            throw new ArgumentException("Email is required and cannot exceed 320 characters.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 1000)
        {
            throw new ArgumentException("PasswordHash is required and cannot exceed 1000 characters.", nameof(passwordHash));
        }

        var trimmedEmail = email.Trim();
        return new UserCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = trimmedEmail,
            NormalizedEmail = NormalizeEmail(trimmedEmail),
            PasswordHash = passwordHash,
            PasswordChangedAtUtc = now
        };
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } lockoutEnd && lockoutEnd > now;

    public void RecordFailedAccess(DateTimeOffset now, int maxFailedAccessAttempts, TimeSpan lockoutDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFailedAccessAttempts, 1);
        if (lockoutDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockoutDuration));
        }

        FailedAccessCount++;
        if (FailedAccessCount >= maxFailedAccessAttempts)
        {
            LockoutEndUtc = now.Add(lockoutDuration);
            FailedAccessCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        LastLoginAtUtc = now;
    }

    public void ReplacePasswordHash(string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 1000)
        {
            throw new ArgumentException("PasswordHash is required and cannot exceed 1000 characters.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
        PasswordChangedAtUtc = now;
    }
}
