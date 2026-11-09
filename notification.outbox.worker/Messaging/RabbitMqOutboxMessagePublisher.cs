using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Outbox.Worker;

public sealed class RabbitMqOutboxMessagePublisher(
    IConfiguration configuration,
    RabbitMqPublishResiliencePolicyProvider resiliencePolicies,
    ILogger<RabbitMqOutboxMessagePublisher> logger)
    : IOutboxMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureChannelAsync(cancellationToken);
            if (connection?.IsOpen != true || channel?.IsOpen != true)
            {
                throw new InvalidOperationException("The RabbitMQ outbox relay channel is not open.");
            }

            // The consumer owns queue topology. A connected producer alone does
            // not prove that its mandatory publications have somewhere to go.
            await channel.QueueDeclarePassiveAsync(
                RabbitMqConfiguration.GetRequiredString(configuration, "CommandQueue"), cancellationToken);
            await channel.QueueDeclarePassiveAsync(
                RabbitMqConfiguration.GetRequiredString(configuration, "RetryQueue"), cancellationToken);
            await channel.QueueDeclarePassiveAsync(
                RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterQueue"), cancellationToken);
        }
        catch
        {
            await ResetAsync();
            throw;
        }
        finally
        {
            publishGate.Release();
        }
    }

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        => PublishAsync(message, null, null, cancellationToken);

    public Task PublishFailureAsync(OutboxMessage message, NotificationFailureKind kind, string errorType,
        CancellationToken cancellationToken)
        => PublishAsync(message, kind, errorType, cancellationToken);

    private Task PublishAsync(OutboxMessage message, NotificationFailureKind? kind, string? errorType,
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString("D"),
            CorrelationId = message.CorrelationId,
            Type = message.MessageType,
            Timestamp = new AmqpTimestamp(message.OccurredAtUtc.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>()
        };

        // Resolve damaged JSON from durable aggregate metadata, never from the
        // unvalidated payload. The consumer verifies this against the outbox row.
        if (message.AggregateType == nameof(BulkNotificationJob) && Guid.TryParse(message.AggregateId, out var jobId) && jobId != Guid.Empty)
            properties.Headers["x-job-id"] = jobId.ToString("D");

        var exchange = RabbitMqConfiguration.GetRequiredString(configuration, "CommandExchange");
        var routingKey = RabbitMqConfiguration.GetRequiredString(configuration, "CommandRoutingKey");
        
        if (kind is { } failure)
        {
            // Relay attempts do not consume the worker's processing budget.
            properties.Headers["x-retry-count"] = 0;
            properties.Headers["x-error-type"] = errorType;
            properties.Headers["x-failure-kind"] = failure.ToString();
            properties.Headers["x-failure-source"] = "outbox";

            var deadLetter = failure == NotificationFailureKind.Permanent;
            // if deadLetter is true, the message will be routed to the dead letter exchange and queue; otherwise, it will be routed to the retry exchange and queue with a delay.
            exchange = RabbitMqConfiguration.GetRequiredString(configuration, deadLetter ? "DeadLetterExchange" : "RetryExchange");
            routingKey = deadLetter ? RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterRoutingKey")
                : $"{RabbitMqConfiguration.GetRequiredString(configuration, "RetryRoutingKey")}.delay.{RabbitMqConfiguration.GetInt32(configuration, "RetryDelayMilliseconds", 1, 3_600_000)}";
        }

        return PublishCoreAsync(
            exchange,
            routingKey,
            properties,
            System.Text.Encoding.UTF8.GetBytes(message.Payload),
            message.Id,
            message.AggregateId,
            cancellationToken);
    }

    private async Task PublishCoreAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        Guid messageId,
        string aggregateId,
        CancellationToken cancellationToken)
    {

        await publishGate.WaitAsync(cancellationToken);
        try
        {
            await resiliencePolicies.Policy.ExecuteAsync(async token =>
            {
                try
                {
                    await EnsureChannelAsync(token);
                    await channel!.BasicPublishAsync(
                        exchange,
                        routingKey,
                        mandatory: true,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: token);
                }
                catch
                {
                    await ResetAsync();
                    throw;
                }
            }, cancellationToken);

            logger.LogInformation(
                "Published durable command {MessageId} for aggregate {AggregateId}. CorrelationId: {CorrelationId}",
                messageId,
                aggregateId,
                properties.CorrelationId);
        }
        finally
        {
            publishGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await publishGate.WaitAsync();
        try
        {
            await ResetAsync();
        }
        finally
        {
            publishGate.Release();
            publishGate.Dispose();
        }
    }

    private async Task EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (connection?.IsOpen == true && channel?.IsOpen == true)
        {
            return;
        }

        await ResetAsync();
        var factory = RabbitMqConnectionFactory.Create(configuration);
        // RequestedConnectionTimeout covers the socket connection but DNS can
        // stall separately when a stopped Compose service loses its DNS entry.
        // Bound the complete connection attempt so readiness notices outages.
        using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionTimeout.CancelAfter(TimeSpan.FromSeconds(
            RabbitMqConfiguration.GetInt32(configuration, "ConnectionTimeoutSeconds", 1, 30)));
        connection = await factory.CreateConnectionAsync("nsa-outbox-relay", connectionTimeout.Token);
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, configuration, cancellationToken);
    }

    private async Task ResetAsync()
    {
        if (channel is not null)
        {
            try
            {
                if (channel.IsOpen)
                {
                    await channel.CloseAsync();
                }
            }
            catch
            {
                // The original broker error is more useful than a cleanup error.
            }

            await channel.DisposeAsync();
            channel = null;
        }

        if (connection is not null)
        {
            try
            {
                if (connection.IsOpen)
                {
                    await connection.CloseAsync();
                }
            }
            catch
            {
                // The original broker error is more useful than a cleanup error.
            }

            await connection.DisposeAsync();
            connection = null;
        }
    }
}
