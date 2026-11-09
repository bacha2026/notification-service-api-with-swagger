using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Service;

/// <summary>Consumes main-queue jobs and delegates durable notification creation to the API processor.</summary>
public sealed class BulkNotificationQueueWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BulkNotificationQueueWorker> logger) : BackgroundService
{
    public static void ValidateConfiguration(IConfiguration configuration)
    {
        foreach (var key in new[] { "HostName", "UserName", "Password", "VirtualHost", "CommandExchange", "CommandQueue", "CommandRoutingKey", "RetryExchange", "RetryQueue", "RetryRoutingKey", "DeadLetterExchange", "DeadLetterQueue", "DeadLetterRoutingKey" })
        {
            if (string.IsNullOrWhiteSpace(configuration[$"RabbitMq:{key}"]))
                throw new InvalidOperationException($"RabbitMq:{key} is required when the queue consumer is enabled.");
        }

        var retryDelay = ReadValidatedInt(configuration, "RetryDelayMilliseconds", 1, 3_600_000);
        var maximumDelay = ReadValidatedInt(configuration, "MaximumRetryDelayMilliseconds", 1, 3_600_000);
        if (maximumDelay < retryDelay)
            throw new InvalidOperationException("RabbitMq:MaximumRetryDelayMilliseconds must be at least RetryDelayMilliseconds.");
        _ = ReadValidatedInt(configuration, "MaxDeliveryAttempts", 1, 20);
        _ = ReadValidatedInt(configuration, "PrefetchCount", 1, 1_000);
    }

    private const string RetryHeader = "x-retry-count";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ConsumeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Bulk notification queue consumer failed; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(Int("NetworkRecoverySeconds", 5, 1, 300)), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        var factory = new ConnectionFactory
        {
            HostName = Required("HostName"), Port = Int("Port", 5672, 1, 65_535),
            UserName = Required("UserName"), Password = Required("Password"), VirtualHost = Required("VirtualHost"),
            AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(Int("RequestedHeartbeatSeconds", 30, 5, 600)),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(Int("NetworkRecoverySeconds", 5, 1, 300)),
            ConsumerDispatchConcurrency = 1
        };
        await using var connection = await factory.CreateConnectionAsync("nsa-api-bulk-consumer", token);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true, consumerDispatchConcurrency: 1), token);
        await DeclareTopologyAsync(channel, token);
        await channel.BasicQosAsync(0, checked((ushort)Int("PrefetchCount", 1, 1, 1000)), false, token);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try { await ProcessAsync(channel, delivery, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not settle main-queue delivery {DeliveryTag}; requeueing it.", delivery.DeliveryTag);
                if (channel.IsOpen) await channel.BasicNackAsync(delivery.DeliveryTag, false, true, token);
            }
        };
        await channel.BasicConsumeAsync(Required("CommandQueue"), autoAck: false, consumer, token);
        logger.LogInformation("Notification API is consuming main queue {Queue}.", Required("CommandQueue"));
        while (connection.IsOpen && channel.IsOpen && !token.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromSeconds(5), token);
    }

    private async Task ProcessAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken token)
    {
        var attempts = ReadAttempt(delivery.BasicProperties.Headers);
        BulkNotificationRequestedV1? command = null;
        Exception? failure = null;
        try
        {
            if (!BulkNotificationCommandReader.TryRead(delivery.Body, delivery.BasicProperties, out command, out var reason))
                throw new PermanentMessageException($"The command envelope is {reason}.");
            if (attempts >= Int("MaxDeliveryAttempts", 3, 1, 20))
                throw new PermanentMessageException("The processing attempt limit has been reached.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
            var result = await processor.ProcessAsync(command.MessageId, command.JobId, token);
            if (result == BulkNotificationProcessingResult.AlreadyDeadLettered)
            {
                await PublishAsync(channel, delivery, attempts, deadLetter: true, "AlreadyDeadLettered",
                    NotificationFailureKind.Permanent, token);
                return;
            }
            await channel.BasicAckAsync(delivery.DeliveryTag, false, token);
            return;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { failure = exception; }

        var maxAttempts = Int("MaxDeliveryAttempts", 3, 1, 20);
        var completedAttempts = attempts >= maxAttempts ? maxAttempts : attempts + 1;
        var kind = Classify(failure!);
        var deadLetter = kind == NotificationFailureKind.Permanent || completedAttempts >= maxAttempts;
        var errorType = failure?.GetType().Name ?? "Unknown";

        try
        {
            if (command is not null)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
                if (deadLetter && !await processor.MarkDeadLetteredAsync(command.JobId, completedAttempts, token))
                {
                    await channel.BasicAckAsync(delivery.DeliveryTag, false, token);
                    return;
                }
                if (!deadLetter) await processor.RecordRetryAsync(command.JobId, completedAttempts, Int("MaxDeliveryAttempts", 3, 1, 20), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception statusException)
        {
            logger.LogWarning(statusException, "Could not save failure status for job {JobId}.", command?.JobId);
            if (deadLetter)
            {
                deadLetter = false;
                completedAttempts = Int("MaxDeliveryAttempts", 3, 1, 20);
                errorType = $"TerminalStatusPersistence:{statusException.GetType().Name}";
            }
        }

        await PublishAsync(channel, delivery, completedAttempts, deadLetter, errorType, kind, token);
        logger.LogWarning(failure, "Bulk command {MessageId} routed to {Destination} after attempt {Attempt}.",
            delivery.BasicProperties.MessageId, deadLetter ? "DLQ" : "retry", completedAttempts);
    }

    private async Task PublishAsync(IChannel channel, BasicDeliverEventArgs delivery, int attempts, bool deadLetter,
        string errorType, NotificationFailureKind kind, CancellationToken token)
    {
        var headers = delivery.BasicProperties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(delivery.BasicProperties.Headers);
        headers[RetryHeader] = attempts;
        headers["x-error-type"] = errorType;
        headers["x-failure-kind"] = kind.ToString();
        headers["x-failure-source"] = "api";
        var maximumAttempts = Int("MaxDeliveryAttempts", 3, 1, 20);
        var backoffAttempt = Math.Clamp(attempts - 1, 0, maximumAttempts - 1);
        var delay = kind == NotificationFailureKind.Timeout
            ? Int("RetryDelayMilliseconds", 5000, 1, 3_600_000)
            : (int)Math.Min((long)Int("RetryDelayMilliseconds", 5000, 1, 3_600_000) << backoffAttempt,
                Int("MaximumRetryDelayMilliseconds", 300000, 1, 3_600_000));
        var properties = new BasicProperties
        {
            ContentType = delivery.BasicProperties.ContentType,
            ContentEncoding = delivery.BasicProperties.ContentEncoding,
            DeliveryMode = DeliveryModes.Persistent,
            Priority = delivery.BasicProperties.Priority,
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            ReplyTo = delivery.BasicProperties.ReplyTo,
            Type = delivery.BasicProperties.Type,
            Timestamp = delivery.BasicProperties.Timestamp,
            UserId = delivery.BasicProperties.UserId,
            AppId = delivery.BasicProperties.AppId,
            ClusterId = delivery.BasicProperties.ClusterId,
            Headers = headers
        };
        var exchange = Required(deadLetter ? "DeadLetterExchange" : "RetryExchange");
        var routingKey = deadLetter ? Required("DeadLetterRoutingKey") : $"{Required("RetryRoutingKey")}.delay.{delay}";
        await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, delivery.Body, token);
        await channel.BasicAckAsync(delivery.DeliveryTag, false, token);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken token)
    {
        var commandExchange = Required("CommandExchange");
        var retryExchange = Required("RetryExchange");
        var deadExchange = Required("DeadLetterExchange");
        foreach (var exchange in new[] { commandExchange, retryExchange, deadExchange })
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: token);

        await DeclareQueueAsync(channel, Required("CommandQueue"), commandExchange, Required("CommandRoutingKey"), new()
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = 20,
            ["x-dead-letter-exchange"] = deadExchange,
            ["x-dead-letter-routing-key"] = Required("DeadLetterRoutingKey")
        }, token);
        await DeclareQueueAsync(channel, Required("RetryQueue"), retryExchange, Required("RetryRoutingKey"), new() { ["x-queue-type"] = "quorum", ["x-delivery-limit"] = -1 }, token);
        await DeclareQueueAsync(channel, Required("DeadLetterQueue"), deadExchange, Required("DeadLetterRoutingKey"), new() { ["x-queue-type"] = "quorum" }, token);
        var maxAttempts = Int("MaxDeliveryAttempts", 3, 1, 20);
        var initial = Int("RetryDelayMilliseconds", 5000, 1, 3_600_000);
        var maximum = Int("MaximumRetryDelayMilliseconds", 300000, initial, 3_600_000);
        foreach (var delay in Enumerable.Range(0, maxAttempts).Select(attempt => Math.Min((long)initial << Math.Clamp(attempt, 0, 19), maximum)).Distinct())
            await DeclareQueueAsync(channel, $"{Required("RetryQueue")}.delay.{delay}", retryExchange,
                $"{Required("RetryRoutingKey")}.delay.{delay}", new()
                {
                    ["x-queue-type"] = "quorum", ["x-message-ttl"] = delay,
                    ["x-dead-letter-exchange"] = retryExchange, ["x-dead-letter-routing-key"] = Required("RetryRoutingKey"),
                    ["x-dead-letter-strategy"] = "at-least-once", ["x-overflow"] = "reject-publish"
                }, token);
    }

    private static async Task DeclareQueueAsync(IChannel channel, string name, string exchange, string routingKey,
        Dictionary<string, object?> arguments, CancellationToken token)
    {
        await channel.QueueDeclareAsync(name, true, false, false, arguments, cancellationToken: token);
        await channel.QueueBindAsync(name, exchange, routingKey, cancellationToken: token);
    }

    private string Required(string key) => configuration[$"RabbitMq:{key}"] is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"RabbitMq:{key} is required.");
    private int Int(string key, int fallback, int min, int max) => int.TryParse(configuration[$"RabbitMq:{key}"], out var value) && value >= min && value <= max ? value : fallback;
    private static int ReadAttempt(IDictionary<string, object?>? headers) => headers is not null && headers.TryGetValue(RetryHeader, out var value)
        ? value switch { byte b => b, short s when s >= 0 => s, int i when i >= 0 => i, long l when l is >= 0 and <= int.MaxValue => (int)l, byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var n) && n >= 0 => n, _ => 0 }
        : 0;

    private static int ReadValidatedInt(IConfiguration configuration, string key, int minimum, int maximum)
    {
        if (!int.TryParse(configuration[$"RabbitMq:{key}"], out var value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"RabbitMq:{key} must be an integer between {minimum} and {maximum}.");
        return value;
    }

    private static NotificationFailureKind Classify(Exception exception)
    {
        if (exception is JsonException or PermanentMessageException or RequestValidationException)
            return NotificationFailureKind.Permanent;
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable })
            return NotificationFailureKind.Backoff;
        if (exception is TimeoutException or OperationCanceledException
            or HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout }
            or SqlException { Number: -2 })
            return NotificationFailureKind.Timeout;
        return exception.InnerException is { } inner ? Classify(inner) : NotificationFailureKind.Unknown;
    }
}
