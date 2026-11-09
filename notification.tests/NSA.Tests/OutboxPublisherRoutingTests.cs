extern alias outbox;

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using outbox::NSA.Outbox.Worker;

namespace NSA.Tests;

public sealed class OutboxPublisherRoutingTests
{
    [Theory]
    [InlineData(null, "main", "main.key")]
    [InlineData(NotificationFailureKind.Permanent, "dead", "dead.key")]
    [InlineData(NotificationFailureKind.Timeout, "retry", "retry.key.delay.5000")]
    [InlineData(NotificationFailureKind.Backoff, "retry", "retry.key.delay.5000")]
    [InlineData(NotificationFailureKind.Unknown, "retry", "retry.key.delay.5000")]
    public async Task Publisher_confirms_the_correct_persistent_destination_and_preserves_the_original_payload(
        NotificationFailureKind? failure, string exchange, string routingKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:CommandExchange"] = "main", ["RabbitMq:CommandRoutingKey"] = "main.key",
            ["RabbitMq:RetryExchange"] = "retry", ["RabbitMq:RetryRoutingKey"] = "retry.key",
            ["RabbitMq:DeadLetterExchange"] = "dead", ["RabbitMq:DeadLetterRoutingKey"] = "dead.key",
            ["RabbitMq:RetryDelayMilliseconds"] = "5000",
            ["RabbitMq:PublishRetryCount"] = "0", ["RabbitMq:InitialPublishRetryDelayMilliseconds"] = "1",
            ["RabbitMq:PublishCircuitBreakerFailures"] = "3", ["RabbitMq:PublishCircuitBreakDurationSeconds"] = "30"
        }).Build();
        var channel = DispatchProxy.Create<IChannel, BrokerProxy>();
        var broker = (BrokerProxy)channel;
        var connection = DispatchProxy.Create<IConnection, BrokerProxy>();
        var logger = NullLogger<RabbitMqOutboxMessagePublisher>.Instance;
        await using var publisher = new RabbitMqOutboxMessagePublisher(configuration,
            new RabbitMqPublishResiliencePolicyProvider(configuration, logger), logger);
        typeof(RabbitMqOutboxMessagePublisher).GetField("channel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(publisher, channel);
        typeof(RabbitMqOutboxMessagePublisher).GetField("connection", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(publisher, connection);
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(), AggregateType = "BulkNotificationJob", AggregateId = Guid.NewGuid().ToString("D"),
            Payload = "{", MessageType = BulkNotificationRequestedV1.MessageType,
            CorrelationId = "correlation", OccurredAtUtc = DateTimeOffset.UtcNow,
            Exchange = "legacy.exchange", RoutingKey = "legacy.key"
        };

        var publishing = failure is { } kind
            ? publisher.PublishFailureAsync(message, kind, "TestFailure", CancellationToken.None)
            : publisher.PublishAsync(message, CancellationToken.None);

        Assert.False(publishing.IsCompleted);
        Assert.Equal(exchange, broker.Exchange);
        Assert.Equal(routingKey, broker.RoutingKey);
        Assert.True(broker.Mandatory);
        Assert.Equal(message.Payload, Encoding.UTF8.GetString(broker.Body.Span));
        var properties = Assert.IsType<BasicProperties>(broker.Properties);
        Assert.Equal(DeliveryModes.Persistent, properties.DeliveryMode);
        Assert.Equal(message.Id.ToString("D"), properties.MessageId);
        Assert.Equal(message.CorrelationId, properties.CorrelationId);
        Assert.Null(properties.Expiration);
        Assert.Equal(message.AggregateId, properties.Headers!["x-job-id"]);
        if (failure is { } expected)
        {
            Assert.Equal(0, properties.Headers["x-retry-count"]);
            Assert.Equal(expected.ToString(), properties.Headers["x-failure-kind"]);
            Assert.Equal("outbox", properties.Headers["x-failure-source"]);
        }

        broker.Confirmation.SetResult();
        await publishing;
    }

    public class BrokerProxy : DispatchProxy
    {
        public TaskCompletionSource Confirmation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Exchange { get; private set; }
        public string? RoutingKey { get; private set; }
        public bool Mandatory { get; private set; }
        public object? Properties { get; private set; }
        public ReadOnlyMemory<byte> Body { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_IsOpen": return true;
                case nameof(IChannel.BasicPublishAsync):
                    Exchange = (string)args![0]!;
                    RoutingKey = (string)args[1]!;
                    Mandatory = (bool)args[2]!;
                    Properties = args[3];
                    Body = (ReadOnlyMemory<byte>)args[4]!;
                    return new ValueTask(Confirmation.Task);
                case nameof(IChannel.CloseAsync): return Task.CompletedTask;
                case nameof(IAsyncDisposable.DisposeAsync): return ValueTask.CompletedTask;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
