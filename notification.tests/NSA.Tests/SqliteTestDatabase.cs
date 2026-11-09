using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace NSA.Tests;

/// <summary>Keeps an isolated relational database alive across test scopes.</summary>
internal sealed class SqliteTestDatabase<TContext> : IDisposable where TContext : DbContext
{
    private readonly SqliteConnection connection = new($"Data Source=tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");

    public SqliteTestDatabase()
    {
        connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Configure(DbContextOptionsBuilder options) => options
        .UseSqlite(connection.ConnectionString)
        .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>();

    public DbContextOptions<TContext> Options
    {
        get
        {
            var builder = new DbContextOptionsBuilder<TContext>();
            Configure(builder);
            return builder.Options;
        }
    }

    public TContext CreateContext() => (TContext)Activator.CreateInstance(typeof(TContext), Options)!;

    public void Dispose() => connection.Dispose();
}

internal static class SqliteTestServices
{
    public static void AddSqliteTestDatabase<TContext>(this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configure = null) where TContext : DbContext
    {
        services.AddSingleton(_ => new SqliteTestDatabase<TContext>());
        services.AddDbContext<TContext>((provider, options) =>
        {
            provider.GetRequiredService<SqliteTestDatabase<TContext>>().Configure(options);
            configure?.Invoke(options);
        });
    }
}

// SQLite cannot sort or compare DateTimeOffset natively. Store UTC ticks in tests
// so relational date predicates retain their ordering and full precision.
internal sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var converter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.GetColumnType() == "nvarchar(max)")
                property.SetColumnType("TEXT");
            if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(converter);
        }
    }
}
