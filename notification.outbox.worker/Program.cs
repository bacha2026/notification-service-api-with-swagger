using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Infrastructure.Configuration;
using NSA.Infrastructure.Messaging;
using NSA.Outbox.Worker;
using NSA.Workers.Shared.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Services.AddNotificationWorkerInfrastructure(builder.Configuration);
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value);
builder.Services.AddSingleton<RabbitMqPublishResiliencePolicyProvider>();
builder.Services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxMessagePublisher>();
builder.Services.AddScoped<OutboxRelayService>();
builder.Services.AddHostedService<OutboxRelayWorker>();

await builder.Build().RunAsync();
