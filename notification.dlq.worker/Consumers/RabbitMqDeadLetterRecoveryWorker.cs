using NSA.Dlq.Worker.Handlers;
using NSA.Infrastructure.Messaging;
using NSA.Workers.Shared.Hosting;
using NSA.Workers.Shared.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Dlq.Worker.Consumers;

/// <summary>Hosts the dead-letter recovery consumer.</summary>
public sealed class RabbitMqDeadLetterRecoveryWorker(
    DeadLetterRecoveryDeliveryHandler handler,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RabbitMqDeadLetterRecoveryWorker> logger)
    : RabbitMqConsumerWorkerBase(
        scopeFactory,
        configuration,
        logger,
        readinessFileName: "nsa-dlq-recovery-worker-ready")
{
    protected override string ConsumerName => "nsa-dlq-recovery-worker";

    protected override string QueueName => RabbitMqValue("DeadLetterQueue");

    // Keep recovery ordered and deliberately conservative: a replay is not
    // acknowledged from the DLQ until its recovery publication is confirmed.
    protected override ushort PrefetchCount => 1;

    protected override Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken) =>
        handler.HandleAsync(new RabbitMqDelivery(channel, eventArgs), stoppingToken);

    protected override void LogConsumerStarted() =>
        Logger.LogInformation(
            "Dead-letter recovery worker is consuming {DeadLetterQueue} and replaying eligible commands through {RecoveryQueue}.",
            RabbitMqValue("DeadLetterQueue"),
            RabbitMqValue("RecoveryQueue"));
}
