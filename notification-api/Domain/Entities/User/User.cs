using NSA.Domain.Enums;

namespace NSA.Domain.Entities;

/// <summary>
/// Represents the profile portion of an account. Authentication secrets live in
/// <see cref="UserCredential"/> so profile data never needs to expose them.
/// </summary>
public sealed class User
{
    private readonly List<UserExternalIdentity> externalIdentities = [];
    private readonly List<UserMfaMethod> mfaMethods = [];

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string? Address { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public Gender Gender { get; private set; }
    public string? ContactNumber { get; private set; }
    public string? PhotoUrl { get; private set; }
    public bool IsAdmin { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public UserCredential? Credential { get; private set; }
    public IReadOnlyCollection<UserExternalIdentity> ExternalIdentities => externalIdentities;
    public IReadOnlyCollection<UserMfaMethod> MfaMethods => mfaMethods;

    public static User Create(
        string firstName,
        string lastName,
        string? address,
        DateOnly? dateOfBirth,
        Gender gender,
        string? contactNumber,
        string? photoUrl,
        DateTimeOffset now)
    {
        ValidateProfile(firstName, lastName, address, dateOfBirth, gender, contactNumber, photoUrl, now);

        return new User
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Address = TrimOptional(address),
            DateOfBirth = dateOfBirth,
            Gender = gender,
            ContactNumber = TrimOptional(contactNumber),
            PhotoUrl = TrimOptional(photoUrl),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void SetCredential(UserCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.UserId != Id)
        {
            throw new ArgumentException("The credential must belong to this user.", nameof(credential));
        }

        Credential = credential;
    }

    public void AddExternalIdentity(UserExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.UserId != Id)
        {
            throw new ArgumentException("The external identity must belong to this user.", nameof(identity));
        }

        externalIdentities.Add(identity);
    }

    public void AddMfaMethod(UserMfaMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.UserId != Id)
        {
            throw new ArgumentException("The MFA method must belong to this user.", nameof(method));
        }

        mfaMethods.Add(method);
    }

    public bool HasEnabledMfaMethod() => mfaMethods.Any(method => method.IsEnabled);

    private static void ValidateProfile(
        string firstName,
        string lastName,
        string? address,
        DateOnly? dateOfBirth,
        Gender gender,
        string? contactNumber,
        string? photoUrl,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("FirstName and LastName are required.");
        }

        if (firstName.Trim().Length > 100 || lastName.Trim().Length > 100)
        {
            throw new ArgumentException("FirstName and LastName cannot exceed 100 characters.");
        }

        if (address?.Trim().Length > 500 || contactNumber?.Trim().Length > 50 || photoUrl?.Trim().Length > 1000)
        {
            throw new ArgumentException("Address, ContactNumber, or PhotoUrl exceeds its maximum length.");
        }

        if (dateOfBirth is { } value && value > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new ArgumentException("DateOfBirth cannot be in the future.");
        }

        if (!Enum.IsDefined(gender))
        {
            throw new ArgumentOutOfRangeException(nameof(gender), gender, "Gender value is not defined.");
        }
    }

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
