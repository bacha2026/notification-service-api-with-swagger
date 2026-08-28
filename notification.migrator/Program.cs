using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSA.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

var connectionString = builder.Configuration.GetConnectionString("NotificationDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:NotificationDb is required for the one-shot database migrator.");
}

var options = new DbContextOptionsBuilder<NotificationDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var dbContext = new NotificationDbContext(options);
var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();

if (pendingMigrations.Length == 0)
{
    Console.WriteLine("Database schema is current; no migrations were applied.");
    return;
}

Console.WriteLine($"Applying {pendingMigrations.Length} additive migration(s).");
await dbContext.Database.MigrateAsync();
Console.WriteLine("Database migrations completed successfully.");
