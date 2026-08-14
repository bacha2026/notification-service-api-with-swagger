using System.ComponentModel.DataAnnotations;
using NSA.Common.Constants;

namespace NSA.Infrastructure.Configuration;

/// <summary>Configuration-bound notification-recipient defaults for the host.</summary>
public sealed class NotificationRecipientOptions
{
    public const string SectionName = "NotificationEmails";

    [Required, EmailAddress]
    public string AdminEmail { get; init; } = NotificationDefaults.AdminEmail;

    [Required, EmailAddress]
    public string DefaultVisitorEmail { get; init; } = NotificationDefaults.VisitorEmail;
}
