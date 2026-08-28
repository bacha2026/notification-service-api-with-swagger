using NSA.Application.Abstractions;
using NSA.Infrastructure.Configuration;

namespace NSA.Outbox.Worker;

public sealed class OutboxRelayWorker(
    IServiceScopeFactory scopeFactory,
    IOutboxMessagePublisher publisher,
    OutboxOptions options,
    IConfiguration configuration,
    ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    private readonly string readinessFile = configuration["Worker:ReadinessFile"]
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "nsa-outbox-worker-ready")
            : "/tmp/nsa-outbox-worker-ready");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var relay = scope.ServiceProvider.GetRequiredService<OutboxRelayService>();
                var result = await relay.RelayDueAsync(workerId, stoppingToken);

                // A successful publish proves broker connectivity. Probe only
                // when the backlog is empty, while failures retain row-level
                // retry state and backoff without an immediate extra reconnect.
                if (result.Claimed == 0)
                {
                    await publisher.CheckHealthAsync(stoppingToken);
                }

                if (result.Failed == 0)
                {
                    File.WriteAllText(readinessFile, DateTimeOffset.UtcNow.ToString("O"));
                }
                else
                {
                    DeleteReadinessFile();
                }

                if (result.Claimed == 0 || result.Failed > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                DeleteReadinessFile();
                logger.LogError(exception, "The outbox relay loop failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
            }
        }

        DeleteReadinessFile();
    }

    private void DeleteReadinessFile()
    {
        try
        {
            File.Delete(readinessFile);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not remove outbox readiness file {ReadinessFile}.", readinessFile);
        }
    }
}
