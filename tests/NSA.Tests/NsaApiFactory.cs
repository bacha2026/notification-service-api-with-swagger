using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Persistence;

namespace NSA.Tests;

public sealed class NsaApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"NSA-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("RabbitMq:UserName", "test-user");
        builder.UseSetting("RabbitMq:Password", "test-password");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<NotificationDbContext>>();
            services.RemoveAll<NotificationDbContext>();
            services.AddDbContext<NotificationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IBulkNotificationCommandPublisher>();
            services.AddSingleton<IBulkNotificationCommandPublisher, TestBulkNotificationCommandPublisher>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.EnsureCreated();
        return host;
    }

}

internal sealed class TestBulkNotificationCommandPublisher : IBulkNotificationCommandPublisher
{
    public Task PublishAsync(BulkNotificationRequestedV1 message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
