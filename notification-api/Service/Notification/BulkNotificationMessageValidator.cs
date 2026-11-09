using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;

namespace NSA.Service;

public static class BulkNotificationMessageValidator
{
    public static void Validate(BulkNotificationJob job)
    {
        if (job.Id == Guid.Empty || string.IsNullOrWhiteSpace(job.CorrelationId)
            || job.MessageSchemaVersion != BulkNotificationRequestedV1.CurrentSchemaVersion
            || job.TotalCount <= 0 || job.Items.Count != job.TotalCount)
            throw new PermanentMessageException("The persisted job is missing required data or has an unsupported version.");

        foreach (var item in job.Items)
        {
            try
            {
                _ = Notification.Create(item.RecipientEmail, item.Channel, item.Subject, item.Body, item.OrderId, job.QueuedAtUtc);
            }
            catch (ArgumentException)
            {
                throw new PermanentMessageException("A persisted notification contains missing or invalid required data.");
            }
        }
    }
}
