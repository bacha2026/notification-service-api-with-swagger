using NSA.Application.Abstractions;
using NSA.Infrastructure.Email;

namespace NSA.Service;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, GmailSmtpEmailSender gmailSmtpEmailSender) : IEmailSender
{
    public async Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation("Email notification queued for {Recipient}. Subject: {Subject}. Body: {Body}", recipientEmail, subject, body);
        await gmailSmtpEmailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
        logger.LogInformation("Email notification sent to {Recipient}. Subject: {Subject}", recipientEmail, subject);
    }
}
