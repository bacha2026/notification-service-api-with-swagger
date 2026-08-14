using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;

namespace NSA.Workers.Shared.Hosting;

/// <summary>Registers infrastructure common to both notification worker executables.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationWorkerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<NotificationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("NotificationDb")));
        RabbitMqConfiguration.Validate(configuration);
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
