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
}
