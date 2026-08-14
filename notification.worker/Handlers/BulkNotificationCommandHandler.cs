using Microsoft.Extensions.Configuration;
using NSA.Application.Contracts;
using NSA.Infrastructure.Messaging;
using NSA.Service;
using NSA.Workers.Shared.Messaging;
using RabbitMQ.Client;

namespace NSA.Worker.Handlers;

/// <summary>
/// Applies the business delivery semantics for one bulk-notification command.
/// Connection, consumer lifecycle, and unhandled-delivery recovery are owned by
/// the shared worker host.
/// </summary>
public sealed class BulkNotificationCommandHandler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BulkNotificationCommandHandler> logger)
{
    internal const string RetryHeader = "x-retry-count";

    public async Task HandleAsync(IRabbitMqDelivery delivery, CancellationToken cancellationToken)
    {
        var body = delivery.Body.ToArray();
        if (!BulkNotificationCommandReader.TryRead(body, delivery.Properties, out var command, out var invalidReason))
        {
            logger.LogWarning(
                "Rejecting {Reason} bulk notification command {MessageId}.",
                invalidReason,
                delivery.Properties.MessageId);
            await delivery.RejectAsync(requeue: false, cancellationToken);
            return;
        }

        var retryCount = RabbitMqMessageHeaders.GetInt32(delivery.Properties.Headers, RetryHeader);
        BulkNotificationProcessingResult processingResult;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
            processingResult = await processor.ProcessAsync(command.JobId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await HandleProcessingFailureAsync(delivery, body, command, retryCount, exception, cancellationToken);
            return;
        }

        if (processingResult == BulkNotificationProcessingResult.AlreadyDeadLettered)
        {
            await delivery.RejectAsync(requeue: false, cancellationToken);
            logger.LogInformation(
                "Rejected redelivered command {MessageId} for already dead-lettered job {JobId}.",
                command.MessageId,
                command.JobId);
            return;
        }

        await delivery.AcknowledgeAsync(cancellationToken);
        logger.LogInformation(
            "Acknowledged command {MessageId} for job {JobId}. CorrelationId: {CorrelationId}",
            command.MessageId,
            command.JobId,
            command.CorrelationId);
    }

    private async Task HandleProcessingFailureAsync(
        IRabbitMqDelivery delivery,
        ReadOnlyMemory<byte> body,
        BulkNotificationRequestedV1 command,
        int retryCount,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var maxAttempts = RabbitMqConfiguration.GetInt32(configuration, "MaxDeliveryAttempts", 1, 20);
        if (retryCount >= maxAttempts - 1)
        {
            await UpdateJobForDeadLetterAsync(command.JobId, maxAttempts, cancellationToken);
            logger.LogError(
                exception,
                "Dead-lettering command {MessageId} for job {JobId} after {AttemptCount} attempts. CorrelationId: {CorrelationId}",
                command.MessageId,
                command.JobId,
                maxAttempts,
                command.CorrelationId);
            await delivery.RejectAsync(requeue: false, cancellationToken);
            return;
        }

        var completedAttempts = retryCount + 1;
        await UpdateJobForRetryAsync(command.JobId, completedAttempts, maxAttempts, cancellationToken);
        var retryProperties = CreateRetryProperties(delivery.Properties, completedAttempts);
        try
        {
            await delivery.PublishAsync(
                RabbitMqConfiguration.GetRequiredString(configuration, "CommandExchange"),
                RabbitMqConfiguration.GetRequiredString(configuration, "CommandRoutingKey"),
                retryProperties,
                body,
                cancellationToken);
        }
        catch (Exception publishException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                publishException,
                "Retry publication failed for command {MessageId}; the unacknowledged delivery will be requeued.",
                command.MessageId);
            throw;
        }

        await delivery.AcknowledgeAsync(cancellationToken);
        logger.LogWarning(
            exception,
            "Republished command {MessageId} for retry {NextAttempt} of {MaxAttempts}. CorrelationId: {CorrelationId}",
            command.MessageId,
            completedAttempts + 1,
            maxAttempts,
            command.CorrelationId);
    }

    private async Task UpdateJobForRetryAsync(
        Guid jobId,
        int completedAttempts,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
        await processor.RecordRetryAsync(
            jobId,
            completedAttempts,
            maxAttempts,
            cancellationToken);
    }

    private async Task UpdateJobForDeadLetterAsync(Guid jobId, int attempts, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<BulkNotificationProcessor>();
        await processor.MarkDeadLetteredAsync(jobId, attempts, cancellationToken);
    }

    internal static int ReadRetryCount(IDictionary<string, object?>? headers) =>
        RabbitMqMessageHeaders.GetInt32(headers, RetryHeader);

    internal static BasicProperties CreateRetryProperties(IReadOnlyBasicProperties source, int retryCount)
    {
        var headers = RabbitMqMessageHeaders.Clone(source.Headers);
        headers[RetryHeader] = retryCount;
        return RabbitMqMessageProperties.CreatePersistentCopy(source, headers);
    }

}
