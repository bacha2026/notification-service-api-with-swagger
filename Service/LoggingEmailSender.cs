using NSA.Application.Abstractions;
using NSA.Infrastructure.Email;

namespace NSA.Service;

public sealed class LoggingEmailSender(EmailNotificationLogger emailNotificationLogger) : IEmailSender
{
    public Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken)
    {
        emailNotificationLogger.LogQueued(recipientEmail, subject, body);
        return Task.CompletedTask;
    }
}
