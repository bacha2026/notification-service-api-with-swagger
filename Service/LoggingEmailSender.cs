using NSA.Application.Abstractions;

namespace NSA.Service;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation("Email notification queued for {Recipient}. Subject: {Subject}. Body: {Body}", recipientEmail, subject, body);
        return Task.CompletedTask;
    }
}
