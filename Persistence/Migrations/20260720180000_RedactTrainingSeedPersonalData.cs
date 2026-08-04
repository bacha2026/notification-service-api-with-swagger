using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSA.Persistence.Migrations;

/// <summary>
/// Replaces personal addresses in databases that applied the original training
/// seed migration before the source seed values were changed to reserved examples.
/// </summary>
[DbContext(typeof(NotificationDbContext))]
[Migration("20260720180000_RedactTrainingSeedPersonalData")]
public sealed class RedactTrainingSeedPersonalData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.UpdateData(
            table: "Orders",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [1],
            columns: ["VisitorEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);
        migrationBuilder.UpdateData(
            table: "Orders",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [2],
            columns: ["VisitorEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);

        migrationBuilder.UpdateData(
            table: "CartItems",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [1],
            columns: ["VisitorEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);
        migrationBuilder.UpdateData(
            table: "CartItems",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [2],
            columns: ["VisitorEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);

        migrationBuilder.UpdateData(
            table: "Notifications",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [1],
            columns: ["RecipientEmail", "Body"],
            columnTypes: ["nvarchar(320)", "nvarchar(4000)"],
            values:
            [
                "admin@example.test",
                "Order #1 for visitor@example.test. Status: Delivered; Payment: Paid; Fulfillment: AssignedToRider; Delivery: Delivered. Total: PHP 1277.00."
            ]);
        migrationBuilder.UpdateData(
            table: "Notifications",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [2],
            columns: ["RecipientEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);
        migrationBuilder.UpdateData(
            table: "Notifications",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [3],
            columns: ["RecipientEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);
        migrationBuilder.UpdateData(
            table: "Notifications",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [4],
            columns: ["RecipientEmail", "Body"],
            columnTypes: ["nvarchar(320)", "nvarchar(4000)"],
            values:
            [
                "admin@example.test",
                "Order #2 for visitor@example.test. Status: Preparing; Payment: Paid; Fulfillment: Packing; Delivery: WaitingForRider. Total: PHP 1398.00."
            ]);
        migrationBuilder.UpdateData(
            table: "Notifications",
            keyColumns: ["Id"],
            keyColumnTypes: ["int"],
            keyValues: [5],
            columns: ["RecipientEmail"],
            columnTypes: ["nvarchar(320)"],
            values: ["visitor@example.test"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Privacy redaction is intentionally irreversible. A rollback must not
        // reintroduce personal data into a training database.
    }
}
