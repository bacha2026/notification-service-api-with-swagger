using Microsoft.Extensions.Configuration;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;
using RabbitMQ.Client;

namespace NSA.Infrastructure.Messaging;

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
    {
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString("D"),
            CorrelationId = message.CorrelationId,
            Type = message.MessageType,
            Timestamp = new AmqpTimestamp(message.OccurredAtUtc.ToUnixTimeSeconds())
        };

        return PublishCoreAsync(
            message.Exchange,
            message.RoutingKey,
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
        connection = await factory.CreateConnectionAsync("nsa-outbox-relay", cancellationToken);
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

