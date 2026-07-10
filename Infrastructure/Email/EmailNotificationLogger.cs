namespace NSA.Infrastructure.Email;

public sealed class EmailNotificationLogger(ILogger<EmailNotificationLogger> logger)
{
    public void LogQueued(string recipientEmail, string subject, string body)
    {
        logger.LogInformation("Email notification queued for {Recipient}. Subject: {Subject}. Body: {Body}", recipientEmail, subject, body);
    }
}
