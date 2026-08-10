using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;

namespace NSA.Infrastructure.Messaging;

/// <summary>Local-demo-only adapter that turns a configured subject into a processing failure.</summary>
public sealed class ConfiguredBulkNotificationFailureInjector(IOptions<RabbitMqOptions> options)
    : IBulkNotificationFailureInjector
{
    public void ThrowIfTriggered(BulkNotificationJob job)
    {
        var failureInjectionSubject = options.Value.FailureInjectionSubject;
        if (!string.IsNullOrWhiteSpace(failureInjectionSubject)
            && job.Items.Any(item =>
                item.Status == BulkNotificationItemStatuses.Pending
                && string.Equals(item.Subject, failureInjectionSubject, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The opt-in Week 3 poison-message failure was triggered.");
        }
    }
}
