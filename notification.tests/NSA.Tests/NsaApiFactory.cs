using System.Collections.Concurrent;
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
    public Exception? BulkNotificationPublishException { get; init; }
    public int? BulkNotificationMaxTrackedJobs { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("RabbitMq:UserName", "test-user");
        builder.UseSetting("RabbitMq:Password", "test-password");
        if (BulkNotificationMaxTrackedJobs is { } maxTrackedJobs)
        {
            builder.UseSetting("BulkNotifications:MaxTrackedJobs", maxTrackedJobs.ToString());
        }
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<NotificationDbContext>>();
            services.RemoveAll<NotificationDbContext>();
            services.AddDbContext<NotificationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IBulkNotificationCommandPublisher>();
            services.AddSingleton<IBulkNotificationCommandPublisher>(
                new TestBulkNotificationCommandPublisher(BulkNotificationPublishException));
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

internal sealed class TestBulkNotificationCommandPublisher(Exception? publishException = null)
    : IBulkNotificationCommandPublisher
{
    public ConcurrentQueue<BulkNotificationRequestedV1> Messages { get; } = new();

    public Task PublishAsync(BulkNotificationRequestedV1 message, CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);
        return publishException is null
            ? Task.CompletedTask
            : Task.FromException(publishException);
    }
}
