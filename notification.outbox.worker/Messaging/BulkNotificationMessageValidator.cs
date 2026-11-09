
namespace NSA.Outbox.Worker;

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
            if (string.IsNullOrWhiteSpace(item.RecipientEmail) || string.IsNullOrWhiteSpace(item.Subject)
                || string.IsNullOrWhiteSpace(item.Body) || !Enum.IsDefined(item.Channel))
                throw new PermanentMessageException("A persisted notification contains missing or invalid required data.");
        }
    }
}
