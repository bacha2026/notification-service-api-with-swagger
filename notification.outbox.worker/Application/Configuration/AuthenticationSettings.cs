namespace NSA.Application.Configuration;

/// <summary>Immutable account-security settings consumed by authentication workflows.</summary>
public sealed record AuthenticationSettings(int MaxFailedAccessAttempts, TimeSpan LockoutDuration);

