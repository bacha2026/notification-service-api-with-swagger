using NSA.Application.Abstractions;
using NSA.Domain.Entities;

namespace NSA.Service;

public sealed class ConfiguredBulkNotificationFailureInjector(IConfiguration configuration)
    : IBulkNotificationFailureInjector
{
    public void ThrowIfTriggered(BulkNotificationJob job)
    {
        var configuredSubject = configuration["RabbitMq:FailureInjectionSubject"];
        if (!string.IsNullOrWhiteSpace(configuredSubject)
            && job.Items.Any(item => item.Status == BulkNotificationItemStatuses.Pending
                && item.Subject == configuredSubject))
        {
            throw new InvalidOperationException("The opt-in poison-message failure was triggered.");
        }
    }
}
