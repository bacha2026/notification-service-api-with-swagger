extern alias dlq;

using RabbitMQ.Client;
using DeadLetterRecoveryMessageInspector = dlq::NSA.Dlq.Worker.Messaging.DeadLetterRecoveryMessageInspector;

namespace NSA.Tests;

public sealed class DeadLetterRecoveryMessageInspectorTests
{
    [Fact]
    public void Rejected_delivery_from_the_command_queue_is_eligible_for_recovery()
    {
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["x-last-death-reason"] = "rejected",
                ["x-last-death-queue"] = "nsa.notifications.bulk.v1"
            }
        };

        var eligible = DeadLetterRecoveryMessageInspector.WasRejectedFromCommandQueue(
            properties,
            "nsa.notifications.bulk.v1");

        Assert.True(eligible);
    }

    [Fact]
    public void Non_rejected_or_wrong_queue_delivery_is_not_replayed()
    {
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["x-last-death-reason"] = "delivery_limit",
                ["x-last-death-queue"] = "nsa.notifications.bulk.v1"
            }
        };

        var eligible = DeadLetterRecoveryMessageInspector.WasRejectedFromCommandQueue(
            properties,
            "nsa.notifications.bulk.v1");

        Assert.False(eligible);
    }

    [Fact]
    public void Replay_resets_the_main_worker_retry_budget_and_preserves_message_identity()
    {
        var source = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = Guid.NewGuid().ToString("D"),
            CorrelationId = "correlation-id",
            Type = "nsa.notifications.bulk-requested.v1",
            Headers = new Dictionary<string, object?>
            {
                ["x-retry-count"] = 2,
                ["x-delivery-count"] = 3,
                ["x-acquired-count"] = 3,
                ["x-death"] = new List<object>()
            }
        };

        var replay = DeadLetterRecoveryMessageInspector.CreateReplayProperties(source, replayCount: 1);
        var replayHeaders = Assert.IsAssignableFrom<IDictionary<string, object?>>(replay.Headers);

        Assert.Equal(source.MessageId, replay.MessageId);
        Assert.Equal(source.CorrelationId, replay.CorrelationId);
        Assert.Equal(source.Type, replay.Type);
        Assert.Equal(1, DeadLetterRecoveryMessageInspector.ReadHeaderCount(
            replayHeaders,
            DeadLetterRecoveryMessageInspector.ReplayCountHeader));
        Assert.DoesNotContain("x-retry-count", replayHeaders.Keys);
        Assert.DoesNotContain("x-delivery-count", replayHeaders.Keys);
        Assert.DoesNotContain("x-acquired-count", replayHeaders.Keys);
        Assert.Contains("x-death", replayHeaders.Keys);
        Assert.Contains("x-retry-count", source.Headers!.Keys);
        Assert.Contains("x-delivery-count", source.Headers.Keys);
        Assert.Contains("x-acquired-count", source.Headers.Keys);
    }

    [Fact]
    public void Rejected_x_death_entry_with_binary_headers_is_eligible_for_recovery()
    {
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["x-death"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "rejected"u8.ToArray(),
                        ["queue"] = new ReadOnlyMemory<byte>("nsa.notifications.bulk.v1"u8.ToArray())
                    }
                }
            }
        };

        Assert.True(DeadLetterRecoveryMessageInspector.WasRejectedFromCommandQueue(
            properties,
            "nsa.notifications.bulk.v1"));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 3)]
    public void Header_counts_are_non_negative_and_bounded(int source, int expected)
    {
        var headers = new Dictionary<string, object?> { ["count"] = source };

        Assert.Equal(expected, DeadLetterRecoveryMessageInspector.ReadHeaderCount(headers, "count"));
    }

    [Fact]
    public void Parking_properties_preserve_message_metadata_and_do_not_mutate_source_headers()
    {
        var source = new BasicProperties
        {
            ContentType = "application/json",
            CorrelationId = "correlation-id",
            MessageId = Guid.NewGuid().ToString("D"),
            Type = "nsa.notifications.bulk-requested.v1",
            ReplyTo = "reply.queue",
            Headers = new Dictionary<string, object?> { ["x-death"] = new List<object>() }
        };

        var parked = DeadLetterRecoveryMessageInspector.CreateParkingLotProperties(source, "unknown-job");
        var parkedHeaders = Assert.IsAssignableFrom<IDictionary<string, object?>>(parked.Headers);

        Assert.Equal(source.MessageId, parked.MessageId);
        Assert.Equal(source.CorrelationId, parked.CorrelationId);
        Assert.Equal(source.ReplyTo, parked.ReplyTo);
        Assert.Equal(DeliveryModes.Persistent, parked.DeliveryMode);
        Assert.Equal("unknown-job", parkedHeaders["x-dlq-recovery-reason"]);
        Assert.DoesNotContain("x-dlq-recovery-reason", source.Headers!.Keys);
    }
}
