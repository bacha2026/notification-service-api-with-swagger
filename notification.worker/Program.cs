using NSA.Application.Abstractions;
using NSA.Infrastructure.Messaging;
using NSA.Persistence.Concrete;
using NSA.Service;
using NSA.Worker.Consumers;
using NSA.Worker.Handlers;
using NSA.Workers.Shared.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNotificationWorkerInfrastructure(builder.Configuration);
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IBulkNotificationJobRepository, BulkNotificationJobRepository>();
builder.Services.AddSingleton<IBulkNotificationFailureInjector, ConfiguredBulkNotificationFailureInjector>();
builder.Services.AddScoped<BulkNotificationProcessor>();
builder.Services.AddSingleton<BulkNotificationCommandHandler>();
builder.Services.AddHostedService<RabbitMqBulkNotificationWorker>();

await builder.Build().RunAsync();
