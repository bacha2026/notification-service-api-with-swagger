using Microsoft.Extensions.Options;
using NSA.Outbox.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));
RabbitMqConfiguration.Validate(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value);
builder.Services.AddSingleton<RabbitMqPublishResiliencePolicyProvider>();
builder.Services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxMessagePublisher>();
builder.Services.AddScoped<OutboxService>();
builder.Services.AddHostedService<OutboxWorker>();

await builder.Build().RunAsync();
