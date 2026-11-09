using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSA.Persistence;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;

namespace NSA.Tests;

public class NsaApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@tests.invalid";
    public string AdminAccessToken { get; private set; } = string.Empty;

    public async Task AddAdminAsync(string email)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var admin = User.Create("Additional", "Administrator", null, null, Gender.Unspecified, null, null, now);
        admin.SetCredential(UserCredential.Create(admin.Id, email, "test-only-password-hash", now));
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE Users SET IsAdmin = 1 WHERE Id = {admin.Id}");
    }

    public async Task<string> GetAdminAccessTokenAsync(string email)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var admin = await users.GetByNormalizedEmailAsync(UserCredential.NormalizeEmail(email), CancellationToken.None)
            ?? throw new InvalidOperationException("Test admin account was not found.");
        return scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>()
            .Issue(admin, admin.Credential!)
            .AccessToken;
    }
    public int? BulkNotificationMaxTrackedJobs { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("Authentication:Jwt:SigningKey", "test-signing-key-for-notification-api-authentication-12345");
        if (BulkNotificationMaxTrackedJobs is { } maxTrackedJobs)
        {
            builder.UseSetting("BulkNotifications:MaxTrackedJobs", maxTrackedJobs.ToString());
        }
        builder.ConfigureServices(services =>
        {
            // Workflow tests explicitly simulate worker processing after API admission.
            services.AddSingleton<NSA.Application.Abstractions.IBulkNotificationFailureInjector,
                ConfiguredBulkNotificationFailureInjector>();
            services.RemoveAll<DbContextOptions<NotificationDbContext>>();
            services.RemoveAll<NotificationDbContext>();
            services.AddSqliteTestDatabase<NotificationDbContext>();
            services.AddScoped<NSA.Application.Abstractions.IIdempotencyLock, SqliteIdempotencyLock>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        dbContext.Database.EnsureCreated();
        var admin = dbContext.Users.SingleOrDefault(user => user.Credential!.NormalizedEmail == UserCredential.NormalizeEmail(AdminEmail));
        if (admin is null)
        {
            var now = DateTimeOffset.UtcNow;
            admin = User.Create("Test", "Administrator", null, null, Gender.Unspecified, null, null, now);
            admin.SetCredential(UserCredential.Create(admin.Id, AdminEmail, "test-only-password-hash", now));
            dbContext.Users.Add(admin);
            dbContext.SaveChanges();
        }

        dbContext.Database.ExecuteSqlInterpolated($"UPDATE Users SET IsAdmin = 1 WHERE Id = {admin.Id}");
        dbContext.Entry(admin).Reload();
        AdminAccessToken = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>()
            .Issue(admin, admin.Credential!)
            .AccessToken;
        return host;
    }

}
