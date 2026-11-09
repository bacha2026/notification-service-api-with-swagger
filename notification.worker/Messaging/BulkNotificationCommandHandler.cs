using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Worker;

/// <summary>Moves ready retry messages back to the main command exchange.</summary>
public sealed class BulkNotificationCommandHandler(
    IConfiguration configuration,
    ILogger<BulkNotificationCommandHandler> logger)
{
    internal const string RetryHeader = "x-retry-count";

    /// <summary>
    /// Forwards a ready retry to the main command queue, preserving its identity and
    /// attempt count. The retry delivery is acknowledged only after publish succeeds.
    /// Messages with an invalid retry count are sent to the dead-letter queue.
    /// </summary>
    public async Task HandleAsync(IRabbitMqDelivery delivery, CancellationToken cancellationToken)
    {
        var headers = delivery.Properties.Headers;
        var retryCount = 0;
     
        if (headers?.ContainsKey(RetryHeader) == true 
            && !RabbitMqMessageHeaders.TryGetInt32(headers, RetryHeader, out retryCount))
        {
            await ForwardInvalidRetryCountToDeadLetterQueueAsync(delivery, cancellationToken);
            return;
        }

        var properties = CreateRetryProperties(delivery.Properties, retryCount);
        properties.Expiration = null;

        var exchange = RabbitMqConfiguration.GetRequiredString(configuration, "CommandExchange");
        var routingKey = RabbitMqConfiguration.GetRequiredString(configuration, "CommandRoutingKey");
        await delivery.PublishAsync(exchange, routingKey, properties, delivery.Body, cancellationToken);
        await delivery.AcknowledgeAsync(cancellationToken);

        logger.LogInformation(
            "Forwarded ready retry message {MessageId} to the main command queue.",
            delivery.Properties.MessageId);
    }
    /// <summary>
    /// republishes each retry to the main command queue and acknowledges the original only after 
    /// publishing succeeds. It sends messages with an invalid retry count to the dead-letter queue.
    /// </summary>
    /// <param name="delivery"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task ForwardInvalidRetryCountToDeadLetterQueueAsync(
        IRabbitMqDelivery delivery,
        CancellationToken cancellationToken)
    {
        var headers = RabbitMqMessageHeaders.Clone(delivery.Properties.Headers);
        headers["x-forwarder-error"] = "InvalidRetryCount";
        var properties = RabbitMqMessageProperties.CreatePersistentCopy(delivery.Properties, headers);
        properties.Expiration = null;

        var exchange = RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterExchange");
        var routingKey = RabbitMqConfiguration.GetRequiredString(configuration, "DeadLetterRoutingKey");
        await delivery.PublishAsync(exchange, routingKey, properties, delivery.Body, cancellationToken);
        await delivery.AcknowledgeAsync(cancellationToken);

        logger.LogWarning(
            "Forwarded retry message {MessageId} to the dead-letter queue because its retry count is invalid.",
            delivery.Properties.MessageId);
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
