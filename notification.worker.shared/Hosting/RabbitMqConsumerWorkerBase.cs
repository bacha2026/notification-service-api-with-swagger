using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Workers.Shared.Hosting;

/// <summary>
/// Owns the common lifecycle for a durable RabbitMQ consumer: connection,
/// topology declaration, readiness, reconnects, and safe requeue on an
/// unhandled delivery failure. Derived workers only provide queue-specific
/// handling.
/// </summary>
public abstract class RabbitMqConsumerWorkerBase : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger logger;
    private readonly string readinessFile;

    protected RabbitMqConsumerWorkerBase(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger logger,
        string readinessFileName)
    {
        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.logger = logger;
        readinessFile = configuration["Worker:ReadinessFile"]
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Path.GetTempPath(), readinessFileName)
                : $"/tmp/{readinessFileName}");
    }

    /// <summary>Reads a required RabbitMQ setting from the application's configuration.</summary>
    protected string RabbitMqValue(string key) =>
        RabbitMqConfiguration.GetRequiredString(configuration, key);

    /// <summary>Reads a range-validated RabbitMQ integer setting.</summary>
    protected int RabbitMqInteger(string key, int minimum, int maximum) =>
        RabbitMqConfiguration.GetInt32(configuration, key, minimum, maximum);

    /// <summary>Logger shared by the lifecycle and its concrete worker.</summary>
    protected ILogger Logger => logger;

    protected abstract string ConsumerName { get; }

    protected abstract string QueueName { get; }

    protected virtual ushort PrefetchCount => checked((ushort)RabbitMqInteger("PrefetchCount", 1, 1_000));

    protected abstract Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken);

    protected virtual void LogConsumerStarted() =>
        logger.LogInformation("{ConsumerName} is consuming {QueueName}.", ConsumerName, QueueName);

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
                logger.LogError(
                    exception,
                    "{ConsumerName} could not connect or consume; retrying in {RetrySeconds} seconds.",
                    ConsumerName,
                    RabbitMqInteger("InitialConnectionRetrySeconds", 1, 300));
                await Task.Delay(
                    TimeSpan.FromSeconds(RabbitMqInteger("InitialConnectionRetrySeconds", 1, 300)),
                    stoppingToken);
            }
        }

        DeleteReadinessFile();
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        var factory = RabbitMqConnectionFactory.Create(configuration);
        await using var connection = await factory.CreateConnectionAsync(ConsumerName, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true,
                consumerDispatchConcurrency: 1),
            stoppingToken);

        await RabbitMqTopology.DeclareAsync(channel, configuration, stoppingToken);
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, eventArgs) =>
            DispatchDeliveryAsync(channel, eventArgs, stoppingToken);
        await channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer,
            stoppingToken);

        await EnsureDatabaseReadyAsync(stoppingToken);
        WriteReadinessFile();
        LogConsumerStarted();

        while (connection.IsOpen && channel.IsOpen && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await EnsureDatabaseReadyAsync(stoppingToken);
        }

        DeleteReadinessFile();
    }

    private async Task DispatchDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        try
        {
            await HandleDeliveryAsync(channel, eventArgs, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Closing the channel requeues any unacknowledged delivery.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "{ConsumerName} could not handle delivery {DeliveryTag}; returning it to RabbitMQ.",
                ConsumerName,
                eventArgs.DeliveryTag);
            await RequeueDeliveryAsync(channel, eventArgs.DeliveryTag, stoppingToken);
        }
    }

    private async Task RequeueDeliveryAsync(
        IChannel channel,
        ulong deliveryTag,
        CancellationToken stoppingToken)
    {
        try
        {
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(
                    deliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken: stoppingToken);
                return;
            }
        }
        catch (Exception nackException) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(
                nackException,
                "{ConsumerName} could not requeue delivery {DeliveryTag}; closing the channel for broker redelivery.",
                ConsumerName,
                deliveryTag);
        }

        try
        {
            if (channel.IsOpen)
            {
                await channel.CloseAsync(stoppingToken);
            }
        }
        catch (Exception closeException) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(
                closeException,
                "{ConsumerName} could not close its channel after delivery {DeliveryTag} failed.",
                ConsumerName,
                deliveryTag);
        }
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException($"{ConsumerName} cannot reach its SQL Server database.");
        }
    }

    private void WriteReadinessFile()
    {
        File.WriteAllText(readinessFile, DateTimeOffset.UtcNow.ToString("O"));
    }

    private void DeleteReadinessFile()
    {
        try
        {
            File.Delete(readinessFile);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "{ConsumerName} could not remove readiness file {ReadinessFile}.", ConsumerName, readinessFile);
        }
    }
}
