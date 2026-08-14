using NSA.Application.Contracts;
using NSA.Workers.Shared.Messaging;
using RabbitMQ.Client;

namespace NSA.Dlq.Worker.Messaging;

/// <summary>
/// Validates and classifies dead-letter deliveries, and preserves their
/// transport metadata when they are replayed or parked.
/// </summary>
public static class DeadLetterRecoveryMessageInspector
{
    internal const string ReplayCountHeader = "x-dlq-replay-count";
    internal const string RetryCountHeader = "x-retry-count";
    internal const string RecoveryReasonHeader = "x-dlq-recovery-reason";

    internal static int ReadHeaderCount(IDictionary<string, object?>? headers, string headerName) =>
        RabbitMqMessageHeaders.GetInt32(headers, headerName);

    internal static bool TryReadCommand(
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties properties,
        out BulkNotificationRequestedV1 command,
        out string invalidReason)
    {
        if (!BulkNotificationCommandReader.TryRead(body, properties, out command, out var reason))
        {
            command = null!;
            invalidReason = reason == BulkNotificationCommandInvalidReason.Malformed
                ? "malformed-command"
                : "unsupported-command";
            return false;
        }

        invalidReason = string.Empty;
        return true;
    }

    internal static bool WasRejectedFromCommandQueue(IReadOnlyBasicProperties properties, string commandQueue)
    {
        if (properties.Headers is null)
        {
            return false;
        }

        if (RabbitMqMessageHeaders.TryGetString(properties.Headers, "x-last-death-reason", out var lastReason)
            && RabbitMqMessageHeaders.TryGetString(properties.Headers, "x-last-death-queue", out var lastQueue))
        {
            return string.Equals(lastReason, "rejected", StringComparison.Ordinal)
                && string.Equals(lastQueue, commandQueue, StringComparison.Ordinal);
        }

        if (!properties.Headers.TryGetValue("x-death", out var deaths) || deaths is not System.Collections.IEnumerable entries)
        {
            return false;
        }

        foreach (var death in entries)
        {
            if (RabbitMqMessageHeaders.TryGetTableString(death, "reason", out var reason)
                && RabbitMqMessageHeaders.TryGetTableString(death, "queue", out var queue)
                && string.Equals(reason, "rejected", StringComparison.Ordinal)
                && string.Equals(queue, commandQueue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static BasicProperties CreateReplayProperties(IReadOnlyBasicProperties source, int replayCount)
    {
        var headers = RabbitMqMessageHeaders.Clone(source.Headers);
        headers.Remove(RetryCountHeader);
        headers.Remove("x-delivery-count");
        headers.Remove("x-acquired-count");
        headers[ReplayCountHeader] = replayCount;
        return RabbitMqMessageProperties.CreatePersistentCopy(source, headers);
    }

    internal static BasicProperties CreateParkingLotProperties(IReadOnlyBasicProperties source, string reason)
    {
        var headers = RabbitMqMessageHeaders.Clone(source.Headers);
        headers[RecoveryReasonHeader] = reason;
        return RabbitMqMessageProperties.CreatePersistentCopy(source, headers);
    }

}
