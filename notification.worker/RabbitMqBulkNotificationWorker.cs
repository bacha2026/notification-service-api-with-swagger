using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Worker;

/// <summary>Consumes ready retries and safely republishes them to the main command queue.</summary>
public sealed class RabbitMqBulkNotificationWorker(
    BulkNotificationCommandHandler handler,
    IConfiguration configuration,
    ILogger<RabbitMqBulkNotificationWorker> logger) : BackgroundService
{
    private const string ConsumerName = "nsa-retry-forwarder";
    private readonly string readinessFile = configuration["Worker:ReadinessFile"]
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "nsa-worker-ready")
            : "/tmp/nsa-worker-ready");

    private int PrefetchCount => RabbitMqConfiguration.GetInt32(configuration, "PrefetchCount", 1, 1_000);

    internal static IReadOnlyList<string> ConsumerQueues(IConfiguration configuration) =>
        [RabbitMqConfiguration.GetRequiredString(configuration, "RetryQueue")];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                DeleteReadinessFile();
                var retrySeconds = RabbitMqConfiguration.GetInt32(configuration, "InitialConnectionRetrySeconds", 1, 300);
                logger.LogError(exception, "{ConsumerName} could not connect or consume; retrying in {RetrySeconds} seconds.", ConsumerName, retrySeconds);
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), stoppingToken);
            }
        }

        DeleteReadinessFile();
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        var factory = RabbitMqConnectionFactory.Create(configuration);
        await using var connection = await factory.CreateConnectionAsync(ConsumerName, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true, consumerDispatchConcurrency: 1), stoppingToken);

        await RabbitMqTopology.DeclareAsync(channel, configuration, stoppingToken);
        await channel.BasicQosAsync(0, checked((ushort)PrefetchCount), global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                await handler.HandleAsync(new RabbitMqDelivery(channel, eventArgs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Closing the channel returns unacknowledged messages to RabbitMQ.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not forward retry delivery {DeliveryTag}; returning it to RabbitMQ.", eventArgs.DeliveryTag);
                var retrySeconds = RabbitMqConfiguration.GetInt32(configuration, "InitialConnectionRetrySeconds", 1, 300);
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), stoppingToken);
                if (channel.IsOpen)
                {
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                }
            }
        };

        foreach (var queue in ConsumerQueues(configuration))
            await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);

        File.WriteAllText(readinessFile, DateTimeOffset.UtcNow.ToString("O"));
        logger.LogInformation("{ConsumerName} is consuming {QueueNames}.", ConsumerName, string.Join(", ", ConsumerQueues(configuration)));
        while (connection.IsOpen && channel.IsOpen && !stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        DeleteReadinessFile();
    }

    private void DeleteReadinessFile()
    {
        try { File.Delete(readinessFile); }
        catch (IOException exception) { logger.LogWarning(exception, "Could not remove readiness file {ReadinessFile}.", readinessFile); }
    }
}
