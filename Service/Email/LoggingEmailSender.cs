using NSA.Application.Abstractions;
using NSA.Infrastructure.Email;

namespace NSA.Service;

public sealed class LoggingEmailSender(EmailNotificationLogger emailNotificationLogger) : IEmailSender
{
    public Task<EmailDeliveryOutcome> SendAsync(
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        emailNotificationLogger.LogDeliveryNotAttempted();
        return Task.FromResult(EmailDeliveryOutcome.NotAttempted);
    }
}
