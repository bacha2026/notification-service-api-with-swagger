using System.Text;
using System.Text.Json;
using NSA.Application.Contracts;
using RabbitMQ.Client;

namespace NSA.Service;

public enum BulkNotificationCommandInvalidReason { None, Malformed, Unsupported }

public static class BulkNotificationCommandReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryRead(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties properties,
        out BulkNotificationRequestedV1 command, out BulkNotificationCommandInvalidReason reason)
    {
        BulkNotificationRequestedV1? candidate;
        try { candidate = JsonSerializer.Deserialize<BulkNotificationRequestedV1>(body.Span, JsonOptions); }
        catch (JsonException)
        {
            command = null!;
            reason = BulkNotificationCommandInvalidReason.Malformed;
            return false;
        }

        var jobHeader = ReadJobId(properties.Headers);
        if (candidate is null || candidate.SchemaVersion != BulkNotificationRequestedV1.CurrentSchemaVersion
            || candidate.MessageId == Guid.Empty || candidate.JobId == Guid.Empty
            || string.IsNullOrWhiteSpace(candidate.CorrelationId) || candidate.CreatedAtUtc == default
            || properties.Type != BulkNotificationRequestedV1.MessageType
            || properties.MessageId is { } messageId && messageId != candidate.MessageId.ToString("D")
            || jobHeader is { } jobId && jobId != candidate.JobId
            || properties.CorrelationId is { } correlation && correlation != candidate.CorrelationId)
        {
            command = null!;
            reason = BulkNotificationCommandInvalidReason.Unsupported;
            return false;
        }

        command = candidate;
        reason = BulkNotificationCommandInvalidReason.None;
        return true;
    }

    private static Guid? ReadJobId(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-job-id", out var value)) return null;
        var text = value switch
        {
            string header => header,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes => Encoding.UTF8.GetString(bytes.Span),
            _ => null
        };
        return Guid.TryParse(text, out var id) && id != Guid.Empty ? id : null;
    }
}
