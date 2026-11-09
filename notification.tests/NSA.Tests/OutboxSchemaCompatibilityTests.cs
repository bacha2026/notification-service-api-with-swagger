extern alias outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSA.Persistence;
using OutboxDbContext = outbox::NSA.Outbox.Worker.NotificationDbContext;

namespace NSA.Tests;

public sealed class OutboxSchemaCompatibilityTests
{
    [Fact]
    public void Outbox_worker_model_matches_api_owned_schema_for_shared_tables()
    {
        const string connection = "Server=localhost;Database=SchemaContract;Integrated Security=True;TrustServerCertificate=True";
        using var api = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(connection).Options);
        using var relay = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>().UseSqlServer(connection).Options);

        foreach (var entity in relay.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            var apiEntity = Assert.Single(api.Model.GetEntityTypes(), candidate => candidate.GetTableName() == table);
            var store = StoreObjectIdentifier.Table(table!, entity.GetSchema());
            foreach (var property in entity.GetProperties())
            {
                var apiProperty = Assert.Single(apiEntity.GetProperties(), candidate => candidate.GetColumnName(store) == property.GetColumnName(store));
                Assert.Equal(apiProperty.GetColumnType(), property.GetColumnType());
                Assert.Equal(apiProperty.IsNullable, property.IsNullable);
                Assert.Equal(apiProperty.IsConcurrencyToken, property.IsConcurrencyToken);
                Assert.Equal(apiProperty.ValueGenerated, property.ValueGenerated);
            }
        }

        Assert.Equal(new[] { "BulkNotificationJobItems", "BulkNotificationJobs", "Orders", "OutboxMessages" },
            relay.Model.GetEntityTypes().Select(entity => entity.GetTableName()).OrderBy(name => name));
    }
}
