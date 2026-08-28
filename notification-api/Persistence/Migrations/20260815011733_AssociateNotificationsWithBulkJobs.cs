using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSA.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssociateNotificationsWithBulkJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BulkNotificationJobId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BulkNotificationJobItemId",
                table: "Notifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Notifications",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BulkNotificationJobId", "BulkNotificationJobItemId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Notifications",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "BulkNotificationJobId", "BulkNotificationJobItemId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Notifications",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "BulkNotificationJobId", "BulkNotificationJobItemId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Notifications",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "BulkNotificationJobId", "BulkNotificationJobItemId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Notifications",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "BulkNotificationJobId", "BulkNotificationJobItemId" },
                values: new object[] { null, null });

            migrationBuilder.Sql(
                """
                WITH RankedNotifications AS
                (
                    SELECT n.Id,
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY n.RecipientEmail, n.Channel, n.Subject, n.Body, n.OrderId
                               ORDER BY n.Id
                           ) AS MatchNumber
                    FROM Notifications AS n
                ),
                RankedItems AS
                (
                    SELECT i.Id, i.JobId, i.RecipientEmail, i.Channel, i.Subject, i.Body, i.OrderId,
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY i.RecipientEmail, i.Channel, i.Subject, i.Body, i.OrderId
                               ORDER BY i.Id
                           ) AS MatchNumber
                    FROM BulkNotificationJobItems AS i
                )
                UPDATE n
                SET n.BulkNotificationJobId = i.JobId,
                    n.BulkNotificationJobItemId = i.Id
                FROM Notifications AS n
                INNER JOIN RankedNotifications AS rn ON rn.Id = n.Id
                INNER JOIN RankedItems AS i
                    ON i.RecipientEmail = n.RecipientEmail
                    AND i.Channel = n.Channel
                    AND i.Subject = n.Subject
                    AND i.Body = n.Body
                    AND (i.OrderId = n.OrderId OR (i.OrderId IS NULL AND n.OrderId IS NULL))
                    AND i.MatchNumber = rn.MatchNumber;

                INSERT INTO Notifications
                (
                    RecipientEmail,
                    Channel,
                    Subject,
                    Body,
                    OrderId,
                    IsRead,
                    CreatedAtUtc,
                    SentAtUtc,
                    BulkNotificationJobId,
                    BulkNotificationJobItemId
                )
                SELECT i.RecipientEmail,
                       i.Channel,
                       i.Subject,
                       i.Body,
                       i.OrderId,
                       0,
                       COALESCE(j.CompletedAtUtc, j.QueuedAtUtc),
                       NULL,
                       j.Id,
                       i.Id
                FROM BulkNotificationJobItems AS i
                INNER JOIN BulkNotificationJobs AS j ON j.Id = i.JobId
                WHERE j.Status = 'Completed'
                  AND i.Status = 'Succeeded'
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM Notifications AS n
                      WHERE n.BulkNotificationJobItemId = i.Id
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BulkNotificationJobId",
                table: "Notifications",
                column: "BulkNotificationJobId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BulkNotificationJobItemId",
                table: "Notifications",
                column: "BulkNotificationJobItemId",
                unique: true,
                filter: "[BulkNotificationJobItemId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_BulkNotificationJobId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_BulkNotificationJobItemId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BulkNotificationJobId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BulkNotificationJobItemId",
                table: "Notifications");
        }
    }
}
