using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSA.Persistence;

#nullable disable

namespace NSA.Persistence.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20261020000000_AddPasswordResetTokens")]
public partial class AddPasswordResetTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PasswordResetTokenHash", table: "UserCredentials", type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "PasswordResetExpiresAtUtc", table: "UserCredentials", type: "datetimeoffset", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PasswordResetTokenHash", table: "UserCredentials");
        migrationBuilder.DropColumn(name: "PasswordResetExpiresAtUtc", table: "UserCredentials");
    }
}
