using System.Text.Json;
using NSA.Application.Contracts;
using RabbitMQ.Client;

namespace NSA.Workers.Shared.Messaging;

/// <summary>
/// Parses the versioned bulk-notification command envelope used by both queue
/// consumers. The handlers decide whether an invalid command is rejected or
/// parked; this reader only reports validity.
/// </summary>
public static class BulkNotificationCommandReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static bool TryRead(
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties properties,
        out BulkNotificationRequestedV1 command,
        out BulkNotificationCommandInvalidReason invalidReason)
    {
        BulkNotificationRequestedV1? candidate;
        try
        {
            candidate = JsonSerializer.Deserialize<BulkNotificationRequestedV1>(body.Span, SerializerOptions);
        }
        catch (JsonException)
        {
            command = null!;
            invalidReason = BulkNotificationCommandInvalidReason.Malformed;
            return false;
        }

        if (candidate is null
            || candidate.SchemaVersion != BulkNotificationRequestedV1.CurrentSchemaVersion
            || candidate.MessageId == Guid.Empty
            || candidate.JobId == Guid.Empty
            || string.IsNullOrWhiteSpace(candidate.CorrelationId)
            || !string.Equals(properties.Type, BulkNotificationRequestedV1.MessageType, StringComparison.Ordinal))
        {
            command = null!;
            invalidReason = BulkNotificationCommandInvalidReason.Unsupported;
            return false;
        }

        command = candidate;
        invalidReason = BulkNotificationCommandInvalidReason.None;
        return true;
    }
}

public enum BulkNotificationCommandInvalidReason
{
    None,
    Malformed,
    Unsupported
}
