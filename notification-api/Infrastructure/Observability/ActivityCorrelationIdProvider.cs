using System.Diagnostics;
using NSA.Application.Abstractions;

namespace NSA.Infrastructure.Observability;

/// <summary>Maps the host's current diagnostic activity to the application correlation port.</summary>
public sealed class ActivityCorrelationIdProvider : ICorrelationIdProvider
{
    public string? CurrentCorrelationId => Activity.Current?.Id;
}
