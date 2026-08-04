using System.Text.Json;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using RabbitMQ.Client;

namespace NSA.Infrastructure.Messaging;

public sealed class RabbitMqBulkNotificationPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqBulkNotificationPublisher> logger) : IBulkNotificationCommandPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task PublishAsync(BulkNotificationRequestedV1 message, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.MessageId.ToString("D"),
            CorrelationId = message.CorrelationId,
            Type = BulkNotificationRequestedV1.MessageType,
            Timestamp = new AmqpTimestamp(message.CreatedAtUtc.ToUnixTimeSeconds())
        };

        await publishGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureChannelAsync(cancellationToken);
            await channel!.BasicPublishAsync(
                options.Value.CommandExchange,
                options.Value.CommandRoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Published bulk notification command {MessageId} for job {JobId}. CorrelationId: {CorrelationId}",
                message.MessageId,
                message.JobId,
                message.CorrelationId);
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
        var factory = RabbitMqConnectionFactory.Create(options.Value);
        connection = await factory.CreateConnectionAsync("nsa-api-publisher", cancellationToken);
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, options.Value, cancellationToken);
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
