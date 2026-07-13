using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

public interface INotificationDispatcher
{
    Task<Notification> CreateEmailNotificationAsync(string recipientEmail, string subject, string body, int? orderId, CancellationToken cancellationToken);
}
