using Microsoft.Extensions.Hosting;
using NSA.Application.Abstractions;

namespace NSA.Infrastructure.Hosting;

/// <summary>Adapts the generic host shutdown signal to the application port.</summary>
public sealed class HostApplicationStopping(IHostApplicationLifetime hostLifetime) : IApplicationStopping
{
    public CancellationToken StoppingToken => hostLifetime.ApplicationStopping;
}
