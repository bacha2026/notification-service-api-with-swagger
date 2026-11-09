using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSA.Persistence;

#nullable disable

namespace NSA.Persistence.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20261020120000_RemovePlaceholderAdminNotifications")]
public sealed class RemovePlaceholderAdminNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM [Notifications] WHERE [Id] IN (1, 4);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Placeholder administrator notifications are intentionally not restored.
    }
}
