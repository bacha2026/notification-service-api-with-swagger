using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

/// <summary>Registers a local user account.</summary>
public sealed record SignUpRequest
{
    /// <summary>Email address used for sign-in.</summary>
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    /// <summary>Local password. It must meet the documented account password policy.</summary>
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; init; } = string.Empty;

    /// <summary>User's given name.</summary>
    [Required, StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    /// <summary>User's family name.</summary>
    [Required, StringLength(100)]
    public string LastName { get; init; } = string.Empty;

    /// <summary>Optional delivery or contact address.</summary>
    [StringLength(500)]
    public string? Address { get; init; }

    /// <summary>Optional date of birth.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Optional self-identified gender.</summary>
    [EnumDataType(typeof(Gender))]
    public Gender Gender { get; init; } = Gender.Unspecified;

    /// <summary>Optional contact telephone number.</summary>
    [Phone, StringLength(50)]
    public string? ContactNumber { get; init; }

    /// <summary>Optional location of the user's profile photo.</summary>
    [Url, StringLength(1000)]
    public string? PhotoUrl { get; init; }
}

/// <summary>Authenticates a local account.</summary>
public sealed record LoginRequest
{
    /// <summary>Email address used to sign in.</summary>
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    /// <summary>Local password.</summary>
    [Required, StringLength(128, MinimumLength = 1), DataType(DataType.Password)]
    public string Password { get; init; } = string.Empty;
}

/// <summary>The safe profile data returned to a signed-in client.</summary>
public sealed record AuthenticatedUserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhotoUrl,
    bool MfaRequired);

/// <summary>A short-lived bearer token and its associated safe user profile.</summary>
public sealed record AuthenticationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    AuthenticatedUserDto User);

/// <summary>Application-facing value returned by the token issuer.</summary>
public sealed record IssuedAccessToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

