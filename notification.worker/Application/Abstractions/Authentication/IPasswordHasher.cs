namespace NSA.Application.Abstractions;

/// <summary>Application port for one-way password hashing and verification.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    PasswordVerificationOutcome Verify(string passwordHash, string password);

    /// <summary>
    /// Performs an equivalent password-hash verification for an unknown account.
    /// This keeps invalid-email and invalid-password sign-in paths comparable.
    /// </summary>
    void VerifyUnknownAccountPassword(string password);
}

/// <summary>Describes the result of password verification without exposing a hashing implementation.</summary>
public enum PasswordVerificationOutcome
{
    Failed = 0,
    Succeeded = 1,
    SucceededRehashNeeded = 2
}

