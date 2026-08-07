using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
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
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BulkNotificationProcessor>();
builder.Services.AddHostedService<RabbitMqBulkNotificationWorker>();

await builder.Build().RunAsync();
