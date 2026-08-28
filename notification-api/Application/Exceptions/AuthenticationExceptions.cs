namespace NSA.Application.Exceptions;

/// <summary>Returned when a requested email is already associated with an account.</summary>
public sealed class EmailAlreadyRegisteredException : Exception
{
    public EmailAlreadyRegisteredException() : base("An account with that email address already exists.")
    {
    }
}

/// <summary>Returned for any invalid local sign-in attempt without revealing account state.</summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException() : base("Invalid email or password.")
    {
    }
}

/// <summary>Returned when a valid first factor requires a separate MFA challenge before token issuance.</summary>
public sealed class MfaChallengeRequiredException : Exception
{
    public MfaChallengeRequiredException() : base("Multi-factor authentication is required to complete sign-in.")
    {
    }
}
