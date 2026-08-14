using RabbitMQ.Client;

namespace NSA.Workers.Shared.Messaging;

/// <summary>Creates persistent copies of RabbitMQ properties while preserving message identity.</summary>
public static class RabbitMqMessageProperties
{
    public static BasicProperties CreatePersistentCopy(
        IReadOnlyBasicProperties source,
        IDictionary<string, object?> headers) =>
        new()
        {
            ContentType = source.ContentType,
            ContentEncoding = source.ContentEncoding,
            DeliveryMode = DeliveryModes.Persistent,
            Priority = source.Priority,
            MessageId = source.MessageId,
            CorrelationId = source.CorrelationId,
            ReplyTo = source.ReplyTo,
            Expiration = source.Expiration,
            Type = source.Type,
            Timestamp = source.Timestamp,
            UserId = source.UserId,
            AppId = source.AppId,
            ClusterId = source.ClusterId,
            Headers = headers
        };
}
