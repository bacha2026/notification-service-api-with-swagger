namespace NSA.Application.Abstractions;

/// <summary>Application-owned port that exposes graceful-shutdown cancellation.</summary>
public interface IApplicationStopping
{
    CancellationToken StoppingToken { get; }
}
