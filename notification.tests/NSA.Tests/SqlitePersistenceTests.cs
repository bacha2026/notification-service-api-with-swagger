using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSA.Domain.Entities;
using NSA.Persistence;

namespace NSA.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Uncommitted_writes_roll_back_across_contexts()
    {
        using var database = new SqliteTestDatabase<NotificationDbContext>();
        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.CartItems.Add(new CartItem
            {
                VisitorEmail = "rollback@example.test", ProductId = 1, Quantity = 1
            });
            await context.SaveChangesAsync();
            Assert.True(await context.CartItems.AnyAsync(item => item.VisitorEmail == "rollback@example.test"));
        }

        await using var verification = database.CreateContext();
        Assert.False(await verification.CartItems.AnyAsync(item => item.VisitorEmail == "rollback@example.test"));
    }

    [Theory]
    [InlineData("visitor@example.test", 1)] // Duplicate seeded visitor/product pair.
    [InlineData("missing-product@example.test", int.MaxValue)]
    public async Task Database_enforces_unique_and_foreign_key_constraints(string email, int productId)
    {
        using var database = new SqliteTestDatabase<NotificationDbContext>();
        await using var context = database.CreateContext();
        context.CartItems.Add(new CartItem { VisitorEmail = email, ProductId = productId, Quantity = 1 });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(19, Assert.IsType<SqliteException>(exception.InnerException).SqliteErrorCode);
    }
}
