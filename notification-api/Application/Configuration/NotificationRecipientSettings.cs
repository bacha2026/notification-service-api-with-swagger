namespace NSA.Application.Configuration;

/// <summary>Validated recipient defaults consumed by cart and order use cases.</summary>
public sealed record NotificationRecipientSettings(
    string AdminEmail,
    string DefaultVisitorEmail)
{
    public string ResolveVisitorEmail(string? visitorEmail) =>
        string.IsNullOrWhiteSpace(visitorEmail)
            ? DefaultVisitorEmail
            : visitorEmail.Trim();
}
