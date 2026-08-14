using Microsoft.Extensions.Configuration;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;

namespace NSA.Infrastructure.Messaging;

/// <summary>Local-demo-only adapter that turns a configured subject into a processing failure.</summary>
public sealed class ConfiguredBulkNotificationFailureInjector(IConfiguration configuration)
    : IBulkNotificationFailureInjector
{
    public void ThrowIfTriggered(BulkNotificationJob job)
    {
        var failureInjectionSubject = RabbitMqConfiguration.GetOptionalString(configuration, "FailureInjectionSubject");
        if (!string.IsNullOrWhiteSpace(failureInjectionSubject)
            && job.Items.Any(item =>
                item.Status == BulkNotificationItemStatuses.Pending
                && string.Equals(item.Subject, failureInjectionSubject, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The opt-in Week 3 poison-message failure was triggered.");
        }
    }
}
