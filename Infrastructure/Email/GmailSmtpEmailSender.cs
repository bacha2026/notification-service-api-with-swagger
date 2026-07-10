using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace NSA.Infrastructure.Email;

public sealed class GmailSmtpEmailSender(IOptions<SmtpEmailOptions> options)
{
    private readonly SmtpEmailOptions options = options.Value;

    public async Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken)
    {
        ValidateOptions();

        using var message = new MailMessage
        {
            From = new MailAddress(ResolveFromAddress(), options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipientEmail));

        using var smtpClient = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            Credentials = new NetworkCredential(options.UserName, options.Password)
        };

        await smtpClient.SendMailAsync(message, cancellationToken);
    }

    private string ResolveFromAddress()
    {
        return string.IsNullOrWhiteSpace(options.FromAddress)
            ? options.UserName
            : options.FromAddress;
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.Host) ||
            options.Port <= 0 ||
            string.IsNullOrWhiteSpace(options.UserName) ||
            string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("SMTP email settings are incomplete. Configure SmtpEmail:Host, Port, UserName, and Password.");
        }
    }
}
