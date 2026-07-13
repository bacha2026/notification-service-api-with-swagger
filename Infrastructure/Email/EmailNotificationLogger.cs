namespace NSA.Infrastructure.Email;

public sealed class EmailNotificationLogger(ILogger<EmailNotificationLogger> logger)
{
    public void LogDeliveryNotAttempted()
    {
        logger.LogInformation("Email delivery was not attempted because the outbound provider is disabled; notification intent remains pending");
    }
}
