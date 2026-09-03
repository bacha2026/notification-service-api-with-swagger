using NSA.Application.Abstractions;
using NSA.Dlq.Worker.Consumers;
using NSA.Dlq.Worker.Handlers;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Service;
using NSA.Dlq.Worker.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Services.AddNotificationWorkerInfrastructure(builder.Configuration);
builder.Services.AddScoped<IBulkNotificationJobRepository, BulkNotificationJobRepository>();
builder.Services.AddScoped<BulkNotificationDeadLetterRecoveryService>();
builder.Services.AddSingleton<DeadLetterRecoveryDeliveryHandler>();
builder.Services.AddHostedService<RabbitMqDeadLetterRecoveryWorker>();

await builder.Build().RunAsync();
