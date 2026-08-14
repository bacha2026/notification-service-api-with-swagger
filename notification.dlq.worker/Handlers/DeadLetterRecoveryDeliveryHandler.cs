using Microsoft.Extensions.Configuration;
using NSA.Dlq.Worker.Messaging;
using NSA.Infrastructure.Messaging;
using NSA.Service;
using NSA.Workers.Shared.Messaging;

namespace NSA.Dlq.Worker.Handlers;

/// <summary>
/// Applies replay and parking semantics to a single dead-letter delivery.
/// RabbitMQ connection management and fallback requeueing are owned by the
/// shared consumer host.
/// </summary>
public sealed class DeadLetterRecoveryDeliveryHandler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DeadLetterRecoveryDeliveryHandler> logger)
{
    public async Task HandleAsync(IRabbitMqDelivery delivery, CancellationToken cancellationToken)
    {
        var body = delivery.Body;
        if (!DeadLetterRecoveryMessageInspector.WasRejectedFromCommandQueue(
                delivery.Properties,
                RabbitMqConfiguration.GetRequiredString(configuration, "CommandQueue")))
        {
            await MoveToParkingLotAsync(
                delivery,
                body,
                "not-a-rejected-command-delivery",
                cancellationToken);
            return;
        }

        if (!DeadLetterRecoveryMessageInspector.TryReadCommand(
                body,
                delivery.Properties,
                out var command,
                out var invalidReason))
        {
            logger.LogWarning(
                "Parking {Reason} dead-lettered notification command {MessageId}.",
                invalidReason,
                delivery.Properties.MessageId);
            await MoveToParkingLotAsync(delivery, body, invalidReason, cancellationToken);
            return;
        }

        var replayCount = DeadLetterRecoveryMessageInspector.ReadHeaderCount(
            delivery.Properties.Headers,
            DeadLetterRecoveryMessageInspector.ReplayCountHeader);
        var maxReplayAttempts = RabbitMqConfiguration.GetInt32(
            configuration,
            "MaxDeadLetterReplayAttempts",
            1,
            20);
        if (replayCount >= maxReplayAttempts)
        {
            logger.LogWarning(
                "Parking dead-lettered notification command {MessageId} after {ReplayCount} replay attempts.",
                command.MessageId,
                replayCount);
            await MoveToParkingLotAsync(delivery, body, "replay-limit-exhausted", cancellationToken);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var recovery = scope.ServiceProvider.GetRequiredService<BulkNotificationDeadLetterRecoveryService>();
        var recoveryResult = await recovery.PrepareForReplayAsync(command.JobId, cancellationToken);

        switch (recoveryResult)
        {
            case BulkNotificationDeadLetterRecoveryResult.NoRecoveryRequired:
                await delivery.AcknowledgeAsync(cancellationToken);
                return;

            case BulkNotificationDeadLetterRecoveryResult.JobNotFound:
                await MoveToParkingLotAsync(delivery, body, "unknown-job", cancellationToken);
                return;

            case BulkNotificationDeadLetterRecoveryResult.RecoveryPrepared:
                await ReplayAsync(
                    delivery,
                    body,
                    command,
                    replayCount,
                    maxReplayAttempts,
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException(
                    $"Unsupported dead-letter recovery result: {recoveryResult}.");
        }
    }

    private async Task ReplayAsync(
        IRabbitMqDelivery delivery,
        ReadOnlyMemory<byte> body,
        NSA.Application.Contracts.BulkNotificationRequestedV1 command,
        int replayCount,
        int maxReplayAttempts,
        CancellationToken cancellationToken)
    {
        var replayProperties = DeadLetterRecoveryMessageInspector.CreateReplayProperties(
            delivery.Properties,
            replayCount + 1);
        await delivery.PublishAsync(
            RabbitMqConfiguration.GetRequiredString(configuration, "RecoveryExchange"),
            RabbitMqConfiguration.GetRequiredString(configuration, "RecoveryRoutingKey"),
            replayProperties,
            body,
            cancellationToken);
        await delivery.AcknowledgeAsync(cancellationToken);

        logger.LogInformation(
            "Replayed dead-lettered notification command {MessageId} for job {JobId}; replay {ReplayCount} of {MaxReplayAttempts}.",
            command.MessageId,
            command.JobId,
            replayCount + 1,
            maxReplayAttempts);
    }

    private async Task MoveToParkingLotAsync(
        IRabbitMqDelivery delivery,
        ReadOnlyMemory<byte> body,
        string reason,
        CancellationToken cancellationToken)
    {
        var parkingProperties = DeadLetterRecoveryMessageInspector.CreateParkingLotProperties(
            delivery.Properties,
            reason);
        await delivery.PublishAsync(
            RabbitMqConfiguration.GetRequiredString(configuration, "ParkingLotExchange"),
            RabbitMqConfiguration.GetRequiredString(configuration, "ParkingLotRoutingKey"),
            parkingProperties,
            body,
            cancellationToken);
        await delivery.AcknowledgeAsync(cancellationToken);

        logger.LogWarning(
            "Parked dead-letter delivery {DeliveryTag}. Reason: {Reason}",
            delivery.DeliveryTag,
            reason);
    }
}
