using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Infrastructure.Email;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Persistence.Interfaces;
using NSA.Service;
using NSA.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<EmailNotificationLogger>();
builder.Services.AddOptions<PostboundOptions>()
    .Bind(builder.Configuration.GetSection(PostboundOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ApiKey), "Postbound:ApiKey is required when Postbound is enabled.")
    .Validate(options => !options.Enabled || (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps), "Postbound:BaseUrl must be an absolute HTTPS URL when Postbound is enabled.")
    .ValidateOnStart();
builder.Services.AddSingleton<EmailResiliencePolicyProvider>();
builder.Services.AddHttpClient<PostboundEmailSender>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PostboundOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
})
    .AddPolicyHandler((serviceProvider, _) => serviceProvider.GetRequiredService<EmailResiliencePolicyProvider>().CircuitBreaker)
    .AddPolicyHandler((serviceProvider, _) => serviceProvider.GetRequiredService<EmailResiliencePolicyProvider>().Retry)
    .AddPolicyHandler((serviceProvider, _) => serviceProvider.GetRequiredService<EmailResiliencePolicyProvider>().Timeout);
builder.Services.AddScoped<IEmailSender>(serviceProvider => serviceProvider.GetRequiredService<PostboundEmailSender>());
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BulkNotificationProcessor>();
builder.Services.AddHostedService<RabbitMqBulkNotificationWorker>();

await builder.Build().RunAsync();
