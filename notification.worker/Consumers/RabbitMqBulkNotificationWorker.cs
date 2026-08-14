using NSA.Infrastructure.Messaging;
using NSA.Worker.Handlers;
using NSA.Workers.Shared.Hosting;
using NSA.Workers.Shared.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NSA.Worker.Consumers;

/// <summary>Hosts the primary bulk-notification command consumer.</summary>
public sealed class RabbitMqBulkNotificationWorker(
    BulkNotificationCommandHandler handler,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RabbitMqBulkNotificationWorker> logger)
    : RabbitMqConsumerWorkerBase(
        scopeFactory,
        configuration,
        logger,
        readinessFileName: "nsa-worker-ready")
{
    protected override string ConsumerName => "nsa-bulk-worker";

    protected override string QueueName => RabbitMqValue("CommandQueue");

    protected override Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken) =>
        handler.HandleAsync(new RabbitMqDelivery(channel, eventArgs), stoppingToken);

    protected override void LogConsumerStarted() =>
        Logger.LogInformation(
            "Bulk notification worker is consuming {Queue}; failed commands are routed to {DeadLetterQueue}.",
            RabbitMqValue("CommandQueue"),
            RabbitMqValue("DeadLetterQueue"));
}
