using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

/// <summary>Optional application port for controlled bulk-processing fault injection.</summary>
public interface IBulkNotificationFailureInjector
{
    void ThrowIfTriggered(BulkNotificationJob job);
}
