extern alias primary;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using BulkNotificationCommandHandler = primary::NSA.Worker.BulkNotificationCommandHandler;
using IRabbitMqDelivery = primary::NSA.Worker.IRabbitMqDelivery;

namespace NSA.Tests;

public sealed class NotificationFailureRoutingTests
{
    [Fact]
    public async Task Retry_forwarder_publishes_to_main_queue_before_acknowledging()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:CommandExchange"] = "commands",
            ["RabbitMq:CommandRoutingKey"] = "main"
        }).Build();
        var delivery = new RecordingDelivery();
        var handler = new BulkNotificationCommandHandler(configuration, NullLogger<BulkNotificationCommandHandler>.Instance);

        await handler.HandleAsync(delivery, CancellationToken.None);

        var publication = Assert.Single(delivery.Publications);
        Assert.Equal("commands", publication.Exchange);
        Assert.Equal("main", publication.RoutingKey);
        Assert.Equal(delivery.Body.ToArray(), publication.Body);
        Assert.Equal(delivery.Properties.MessageId, publication.Properties.MessageId);
        Assert.Equal(2, publication.Properties.Headers!["x-retry-count"]);
        Assert.Equal(DeliveryModes.Persistent, publication.Properties.DeliveryMode);
        Assert.Null(publication.Properties.Expiration);
        Assert.Equal(new[] { "publish", "ack" }, delivery.Events);
    }

    [Fact]
    public async Task Failed_main_queue_publication_leaves_retry_unacknowledged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:CommandExchange"] = "commands",
            ["RabbitMq:CommandRoutingKey"] = "main"
        }).Build();
        var delivery = new RecordingDelivery { PublishException = new IOException("broker unavailable") };
        var handler = new BulkNotificationCommandHandler(configuration, NullLogger<BulkNotificationCommandHandler>.Instance);

        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(delivery, CancellationToken.None));

        Assert.Equal(new[] { "publish" }, delivery.Events);
    }

    [Fact]
    public async Task Invalid_retry_count_is_sent_to_dead_letter_queue_before_acknowledging()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:DeadLetterExchange"] = "dead-letters",
            ["RabbitMq:DeadLetterRoutingKey"] = "invalid-retry"
        }).Build();
        var delivery = new RecordingDelivery("invalid");
        var handler = new BulkNotificationCommandHandler(configuration, NullLogger<BulkNotificationCommandHandler>.Instance);

        await handler.HandleAsync(delivery, CancellationToken.None);

        var publication = Assert.Single(delivery.Publications);
        Assert.Equal("dead-letters", publication.Exchange);
        Assert.Equal("invalid-retry", publication.RoutingKey);
        Assert.Equal("InvalidRetryCount", publication.Properties.Headers!["x-forwarder-error"]);
        Assert.Equal(new[] { "publish", "ack" }, delivery.Events);
    }

    private sealed class RecordingDelivery : IRabbitMqDelivery
    {
        private readonly byte[] body = "{\"command\":true}"u8.ToArray();
        public RecordingDelivery() : this(2) { }

        public RecordingDelivery(object? retryCount)
        {
            Properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString("D"),
                CorrelationId = "correlation",
                Type = "nsa.notifications.bulk-requested.v1",
                Headers = new Dictionary<string, object?> { ["x-retry-count"] = retryCount }
            };
        }

        public ulong DeliveryTag => 1;
        public ReadOnlyMemory<byte> Body => body;
        public IReadOnlyBasicProperties Properties { get; }
        public Exception? PublishException { get; init; }
        public List<string> Events { get; } = [];
        public List<(string Exchange, string RoutingKey, BasicProperties Properties, byte[] Body)> Publications { get; } = [];
        public Task PublishAsync(string exchange, string routingKey, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            Events.Add("publish");
            Publications.Add((exchange, routingKey, properties, body.ToArray()));
            return PublishException is null ? Task.CompletedTask : Task.FromException(PublishException);
        }
        public Task AcknowledgeAsync(CancellationToken cancellationToken) { Events.Add("ack"); return Task.CompletedTask; }
        public Task RejectAsync(bool requeue, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
