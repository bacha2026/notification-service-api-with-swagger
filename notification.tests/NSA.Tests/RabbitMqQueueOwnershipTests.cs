extern alias primary;
extern alias outbox;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace NSA.Tests;

public sealed class RabbitMqQueueOwnershipTests
{
    [Fact]
    public async Task Delayed_failures_route_to_the_consumed_retry_queue()
    {
        var channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        var recording = (RecordingChannel)channel;
        var configuration = Configuration();

        await primary::NSA.Worker.RabbitMqTopology.DeclareAsync(channel, configuration, CancellationToken.None);

        var main = Assert.Single(recording.Queues, queue => queue.Name == "main.queue");
        Assert.Equal(20, main.Arguments["x-delivery-limit"]);
        Assert.Equal("dead", main.Arguments["x-dead-letter-exchange"]);
        Assert.Equal("dead.key", main.Arguments["x-dead-letter-routing-key"]);
        var ready = Assert.Single(recording.Queues, queue => queue.Name == "retry.queue");
        Assert.False(ready.Arguments.ContainsKey("x-message-ttl"));
        Assert.Equal(-1, ready.Arguments["x-delivery-limit"]);
        Assert.Contains(("retry.queue", "retry", "retry.key"), recording.Bindings);
        foreach (var delay in new[] { 5000, 10000, 20000 })
        {
            var bucket = Assert.Single(recording.Queues, queue => queue.Name == $"retry.queue.delay.{delay}");
            Assert.Equal(delay, bucket.Arguments["x-message-ttl"]);
            Assert.Equal("retry", bucket.Arguments["x-dead-letter-exchange"]);
            Assert.Equal("retry.key", bucket.Arguments["x-dead-letter-routing-key"]);
            Assert.Equal("at-least-once", bucket.Arguments["x-dead-letter-strategy"]);
            Assert.Contains((bucket.Name, "retry", $"retry.key.delay.{delay}"), recording.Bindings);
        }
        Assert.DoesNotContain(recording.Queues, queue => queue.Name == "retry.queue.5000");
    }

    [Fact]
    public async Task Outbox_declares_all_destination_exchanges_but_leaves_queues_to_the_consumer()
    {
        var channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:CommandExchange"] = "main",
            ["RabbitMq:RetryExchange"] = "retry",
            ["RabbitMq:DeadLetterExchange"] = "dead"
        }).Build();

        await outbox::NSA.Outbox.Worker.RabbitMqTopology.DeclareAsync(channel, configuration, CancellationToken.None);

        var recording = (RecordingChannel)channel;
        Assert.Equal(new[] { "main", "retry", "dead" }, recording.Exchanges);
        Assert.Empty(recording.Queues);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:CommandExchange"] = "main",
            ["RabbitMq:CommandQueue"] = "main.queue",
            ["RabbitMq:CommandRoutingKey"] = "main.key",
            ["RabbitMq:RetryExchange"] = "retry",
            ["RabbitMq:RetryQueue"] = "retry.queue",
            ["RabbitMq:RetryRoutingKey"] = "retry.key",
            ["RabbitMq:DeadLetterExchange"] = "dead",
            ["RabbitMq:DeadLetterQueue"] = "dead.queue",
            ["RabbitMq:DeadLetterRoutingKey"] = "dead.key",
            ["RabbitMq:MaxDeliveryAttempts"] = "3",
            ["RabbitMq:RetryDelayMilliseconds"] = "5000",
            ["RabbitMq:MaximumRetryDelayMilliseconds"] = "300000"
        }).Build();

    public class RecordingChannel : DispatchProxy
    {
        public List<string> Exchanges { get; } = [];
        public List<(string Name, IDictionary<string, object?> Arguments)> Queues { get; } = [];
        public List<(string Queue, string Exchange, string RoutingKey)> Bindings { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IChannel.ExchangeDeclareAsync):
                    Exchanges.Add((string)args![0]!);
                    return Task.CompletedTask;
                case nameof(IChannel.QueueDeclareAsync):
                    var queue = (string)args![0]!;
                    Queues.Add((queue, (IDictionary<string, object?>)args[4]!));
                    return Task.FromResult(new QueueDeclareOk(queue, 0, 0));
                case nameof(IChannel.QueueBindAsync):
                    Bindings.Add(((string)args![0]!, (string)args[1]!, (string)args[2]!));
                    return Task.CompletedTask;
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
