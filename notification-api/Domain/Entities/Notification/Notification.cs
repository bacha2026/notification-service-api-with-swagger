using NSA.Domain.Enums;

namespace NSA.Domain.Entities;

public sealed class Notification
{
    public int Id { get; set; }
    public required string RecipientEmail { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }

    public static Notification Create(string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId, DateTimeOffset createdAtUtc)
    {
        Validate(recipientEmail, channel, subject, body);

        return new Notification
        {
            RecipientEmail = recipientEmail.Trim(),
            Channel = channel,
            Subject = subject.Trim(),
            Body = body.Trim(),
            OrderId = orderId,
            IsRead = false,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Update(string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId, bool isRead)
    {
        Validate(recipientEmail, channel, subject, body);

        RecipientEmail = recipientEmail.Trim();
        Channel = channel;
        Subject = subject.Trim();
        Body = body.Trim();
        OrderId = orderId;
        IsRead = isRead;
    }

    public void MarkAsSent(DateTimeOffset sentAtUtc)
    {
        SentAtUtc = sentAtUtc;
    }

    private static void Validate(string recipientEmail, NotificationChannel channel, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("RecipientEmail, Subject, and Body are required.");
        }

        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "NotificationChannel value is not defined.");
        }
    }
}
