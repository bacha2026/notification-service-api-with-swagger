namespace NSA.Domain.Enums;

/// <summary>Supported categories of second-factor authenticators.</summary>
public enum MfaMethodType
{
    AuthenticatorApp = 1,
    Email = 2,
    Sms = 3,
    SecurityKey = 4
}
