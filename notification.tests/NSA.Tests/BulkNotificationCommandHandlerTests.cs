extern alias primary;

using RabbitMQ.Client;
using BulkNotificationCommandHandler = primary::NSA.Worker.Handlers.BulkNotificationCommandHandler;

namespace NSA.Tests;

public sealed class BulkNotificationCommandHandlerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(-1, 0)]
    [InlineData(2147483647, 2147483647)]
    public void Retry_count_reader_accepts_only_non_negative_values(int source, int expected)
    {
        var headers = new Dictionary<string, object?> { ["x-retry-count"] = source };

        Assert.Equal(expected, BulkNotificationCommandHandler.ReadRetryCount(headers));
    }

    [Fact]
    public void Retry_properties_preserve_identity_and_copy_headers_before_setting_the_attempt()
    {
        var source = new BasicProperties
        {
            ContentType = "application/json",
            CorrelationId = "correlation-id",
            MessageId = Guid.NewGuid().ToString("D"),
            Type = "nsa.notifications.bulk-requested.v1",
            Headers = new Dictionary<string, object?>
            {
                ["x-retry-count"] = 1,
                ["x-death"] = new List<object>()
            }
        };

        var retry = BulkNotificationCommandHandler.CreateRetryProperties(source, retryCount: 2);
        var retryHeaders = Assert.IsAssignableFrom<IDictionary<string, object?>>(retry.Headers);

        Assert.Equal(DeliveryModes.Persistent, retry.DeliveryMode);
        Assert.Equal(source.MessageId, retry.MessageId);
        Assert.Equal(source.CorrelationId, retry.CorrelationId);
        Assert.Equal(source.Type, retry.Type);
        Assert.Equal(2, BulkNotificationCommandHandler.ReadRetryCount(retryHeaders));
        Assert.Equal(1, source.Headers!["x-retry-count"]);
        Assert.NotSame(source.Headers, retryHeaders);
    }
}
