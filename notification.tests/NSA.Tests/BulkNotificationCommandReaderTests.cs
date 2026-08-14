using System.Text.Json;
using NSA.Application.Contracts;
using NSA.Workers.Shared.Messaging;
using RabbitMQ.Client;

namespace NSA.Tests;

public sealed class BulkNotificationCommandReaderTests
{
    [Fact]
    public void Valid_versioned_command_is_read_once_by_both_workers()
    {
        var expected = new BulkNotificationRequestedV1(
            BulkNotificationRequestedV1.CurrentSchemaVersion,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "correlation-id",
            DateTimeOffset.UtcNow);
        var body = JsonSerializer.SerializeToUtf8Bytes(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var properties = new BasicProperties { Type = BulkNotificationRequestedV1.MessageType };

        var isValid = BulkNotificationCommandReader.TryRead(body, properties, out var actual, out var reason);

        Assert.True(isValid);
        Assert.Equal(BulkNotificationCommandInvalidReason.None, reason);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Malformed_command_is_classified_without_throwing()
    {
        var properties = new BasicProperties { Type = BulkNotificationRequestedV1.MessageType };

        var isValid = BulkNotificationCommandReader.TryRead("{"u8.ToArray(), properties, out _, out var reason);

        Assert.False(isValid);
        Assert.Equal(BulkNotificationCommandInvalidReason.Malformed, reason);
    }

    [Fact]
    public void Wrong_message_type_is_not_accepted_as_a_bulk_command()
    {
        var command = new BulkNotificationRequestedV1(
            BulkNotificationRequestedV1.CurrentSchemaVersion,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "correlation-id",
            DateTimeOffset.UtcNow);
        var body = JsonSerializer.SerializeToUtf8Bytes(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var properties = new BasicProperties { Type = "different-message-type" };

        var isValid = BulkNotificationCommandReader.TryRead(body, properties, out _, out var reason);

        Assert.False(isValid);
        Assert.Equal(BulkNotificationCommandInvalidReason.Unsupported, reason);
    }
}
