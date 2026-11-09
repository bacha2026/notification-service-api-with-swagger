using NSA.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
RabbitMqConfiguration.Validate(builder.Configuration);//rejects missing credentials and invalid numeric settings at startup
builder.Services.AddSingleton<BulkNotificationCommandHandler>();
builder.Services.AddHostedService<RabbitMqBulkNotificationWorker>();//hosted background service for processing messages from RabbitMQ

await builder.Build().RunAsync();
