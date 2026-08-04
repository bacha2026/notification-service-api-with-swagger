using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Contracts;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;
using NSA.Service;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Worker;

public sealed class RabbitMqBulkNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IConfiguration configuration,
    ILogger<RabbitMqBulkNotificationWorker> logger) : BackgroundService
{
    internal const string RetryHeader = "x-retry-count";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string readinessFile = configuration["Worker:ReadinessFile"]
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "nsa-worker-ready")
            : "/tmp/nsa-worker-ready");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = RabbitMqConnectionFactory.Create(options.Value);
                await using var connection = await factory.CreateConnectionAsync("nsa-bulk-worker", stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true,
                        consumerDispatchConcurrency: 1),
                    stoppingToken);

                await RabbitMqTopology.DeclareAsync(channel, options.Value, stoppingToken);
                await channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: checked((ushort)options.Value.PrefetchCount),
                    global: false,
                    cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, eventArgs) =>
                    HandleDeliveryAsync(channel, eventArgs, stoppingToken);
                await channel.BasicConsumeAsync(
                    options.Value.CommandQueue,
                    autoAck: false,
                    consumer,
                    stoppingToken);

                await EnsureDatabaseReadyAsync(stoppingToken);
                WriteReadinessFile();
                logger.LogInformation(
                    "Bulk notification worker is consuming {Queue}; failed commands are routed to {DeadLetterQueue}.",
                    options.Value.CommandQueue,
                    options.Value.DeadLetterQueue);

                while (connection.IsOpen && channel.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    await EnsureDatabaseReadyAsync(stoppingToken);
                }

                // A cleanly closed connection/channel does not enter the catch block.
                // Clear readiness before reconnecting so health-gated deployments do
                // not treat a disconnected consumer as ready.
                DeleteReadinessFile();
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
                    "Bulk notification worker could not connect or consume; retrying in {RetrySeconds} seconds.",
                    options.Value.InitialConnectionRetrySeconds);
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.InitialConnectionRetrySeconds),
                    stoppingToken);
            }
        }

        DeleteReadinessFile();
    }

    internal async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        try
        {
            await HandleDeliveryCoreAsync(channel, eventArgs, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Closing the channel requeues any delivery that is still unacknowledged.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled delivery failure for tag {DeliveryTag}; returning it to RabbitMQ so prefetch does not stall.",
                eventArgs.DeliveryTag);
            await RequeueAfterUnhandledFailureAsync(channel, eventArgs.DeliveryTag, stoppingToken);
        }
    }

    private async Task HandleDeliveryCoreAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        // RabbitMQ.Client owns the event memory; copy before any asynchronous work.
        var body = eventArgs.Body.ToArray();
        BulkNotificationRequestedV1? command;
        try
        {
            command = JsonSerializer.Deserialize<BulkNotificationRequestedV1>(body, SerializerOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Rejecting malformed bulk notification command {MessageId}.", eventArgs.BasicProperties.MessageId);
            await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        if (command is null
            || command.SchemaVersion != BulkNotificationRequestedV1.CurrentSchemaVersion
            || command.MessageId == Guid.Empty
            || command.JobId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.CorrelationId)
            || !string.Equals(eventArgs.BasicProperties.Type, BulkNotificationRequestedV1.MessageType, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Rejecting unsupported bulk notification command {MessageId}; schema {SchemaVersion}, type {MessageType}.",
                command?.MessageId,
                command?.SchemaVersion,
                eventArgs.BasicProperties.Type);
            await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        var retryCount = ReadRetryCount(eventArgs.BasicProperties.Headers);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
            var disposition = await processor.ProcessAsync(command.JobId, stoppingToken);
            if (disposition == BulkNotificationProcessDisposition.DeadLetter)
            {
                await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, stoppingToken);
                logger.LogInformation(
                    "Rejected redelivered command {MessageId} for already dead-lettered job {JobId}.",
                    command.MessageId,
                    command.JobId);
                return;
            }

            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
            logger.LogInformation(
                "Acknowledged command {MessageId} for job {JobId}. CorrelationId: {CorrelationId}",
                command.MessageId,
                command.JobId,
                command.CorrelationId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Closing the channel requeues the unacknowledged delivery.
        }
        catch (Exception exception)
        {
            var completedAttempts = retryCount + 1;
            if (completedAttempts >= options.Value.MaxDeliveryAttempts)
            {
                await MarkDeadLetteredAsync(command.JobId, completedAttempts, stoppingToken);
                logger.LogError(
                    exception,
                    "Dead-lettering command {MessageId} for job {JobId} after {AttemptCount} attempts. CorrelationId: {CorrelationId}",
                    command.MessageId,
                    command.JobId,
                    completedAttempts,
                    command.CorrelationId);
                await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, stoppingToken);
                return;
            }

            await RecordRetryAsync(command.JobId, completedAttempts, stoppingToken);
            var retryProperties = CreateRetryProperties(eventArgs.BasicProperties, completedAttempts);
            try
            {
                await channel.BasicPublishAsync(
                    options.Value.CommandExchange,
                    options.Value.CommandRoutingKey,
                    mandatory: true,
                    basicProperties: retryProperties,
                    body: body,
                    cancellationToken: stoppingToken);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
                logger.LogWarning(
                    exception,
                    "Republished command {MessageId} for retry {NextAttempt} of {MaxAttempts}. CorrelationId: {CorrelationId}",
                    command.MessageId,
                    completedAttempts + 1,
                    options.Value.MaxDeliveryAttempts,
                    command.CorrelationId);
            }
            catch (Exception publishException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    publishException,
                    "Retry publication failed for command {MessageId}; returning the original delivery to RabbitMQ.",
                    command.MessageId);
                await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: true, stoppingToken);
            }
        }
    }

    private async Task RequeueAfterUnhandledFailureAsync(
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
                "Could not requeue delivery tag {DeliveryTag}; closing the channel to force broker redelivery.",
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
                "Could not close the channel after delivery tag {DeliveryTag} failed.",
                deliveryTag);
        }
    }

    internal static int ReadRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(RetryHeader, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            byte number => number,
            short number => number,
            int number => number,
            long number when number <= int.MaxValue => (int)number,
            byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var number) => number,
            _ => 0
        };
    }

    private static BasicProperties CreateRetryProperties(IReadOnlyBasicProperties source, int retryCount)
    {
        var headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source.Headers);
        headers[RetryHeader] = retryCount;

        return new BasicProperties
        {
            ContentType = source.ContentType,
            ContentEncoding = source.ContentEncoding,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = source.MessageId,
            CorrelationId = source.CorrelationId,
            Type = source.Type,
            Timestamp = source.Timestamp,
            Headers = headers
        };
    }

    private async Task RecordRetryAsync(Guid jobId, int completedAttempts, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
        await processor.RecordRetryAsync(
            jobId,
            completedAttempts,
            options.Value.MaxDeliveryAttempts,
            cancellationToken);
    }

    private async Task MarkDeadLetteredAsync(Guid jobId, int attempts, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
        await processor.MarkDeadLetteredAsync(jobId, attempts, cancellationToken);
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException("The worker cannot reach its SQL Server database.");
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
            logger.LogWarning(exception, "Could not remove worker readiness file {ReadinessFile}.", readinessFile);
        }
    }
}
