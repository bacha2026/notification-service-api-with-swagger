namespace NSA.Application.Abstractions;

public enum EmailDeliveryOutcome
{
    NotAttempted = 0,
    AcceptedByProvider = 1
}

public interface IEmailSender
{
    Task<EmailDeliveryOutcome> SendAsync(
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
