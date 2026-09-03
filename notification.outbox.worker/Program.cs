using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Infrastructure.Configuration;
using NSA.Infrastructure.Messaging;
using NSA.Outbox.Worker;
using NSA.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));
RabbitMqConfiguration.Validate(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<OutboxOptions>()// Create appsettings.json
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value);
builder.Services.AddSingleton<RabbitMqPublishResiliencePolicyProvider>(); //To be decoupled
builder.Services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxMessagePublisher>();//To be decoupled
builder.Services.AddScoped<OutboxRelayService>();
builder.Services.AddHostedService<OutboxRelayWorker>();

await builder.Build().RunAsync();
