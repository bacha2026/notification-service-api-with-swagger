using Microsoft.AspNetCore.Identity;
using NSA.Application.Abstractions;

namespace NSA.Infrastructure.Authentication;

/// <summary>PBKDF2 password-hashing adapter provided by ASP.NET Core Identity.</summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    // Identity V3 uses PBKDF2-HMAC-SHA512. 210,000 iterations tracks the
    // current OWASP guidance for that PRF while retaining a migration path via
    // PasswordVerificationResult.SuccessRehashNeeded.
    private readonly PasswordHasher<object> hasher;
    private readonly string unknownAccountPasswordHash;

    public AspNetPasswordHasher()
    {
        hasher = new PasswordHasher<object>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 210_000
        }));

        // The per-process hash contains a random salt and is only used to make
        // an unknown-email attempt cost the same PBKDF2 verification as a
        // known account. It is never persisted or exposed.
        unknownAccountPasswordHash = hasher.HashPassword(new object(), "unknown-account-verification");
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return hasher.HashPassword(new object(), password);
    }

    public PasswordVerificationOutcome Verify(string passwordHash, string password)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerificationOutcome.Failed;
        }

        return hasher.VerifyHashedPassword(new object(), passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SucceededRehashNeeded,
            _ => PasswordVerificationOutcome.Failed
        };
    }

    public void VerifyUnknownAccountPassword(string password)
    {
        _ = hasher.VerifyHashedPassword(
            new object(),
            unknownAccountPasswordHash,
            password ?? string.Empty);
    }
}
