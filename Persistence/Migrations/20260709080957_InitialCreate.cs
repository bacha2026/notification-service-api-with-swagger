using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NSA.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    OrderStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FulfillmentStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DeliveryStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(280)", maxLength: 280, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    QuantityAvailable = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Orders",
                columns: new[] { "Id", "CreatedAtUtc", "DeliveryStatus", "FulfillmentStatus", "OrderStatus", "PaymentStatus", "TotalAmount", "UpdatedAtUtc", "VisitorEmail" },
                values: new object[,]
                {
                    { 1, new DateTimeOffset(new DateTime(2026, 7, 8, 3, 30, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Delivered", "AssignedToRider", "Delivered", "Paid", 1277.00m, new DateTimeOffset(new DateTime(2026, 7, 8, 5, 15, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "bmacha2015@gmail.com" },
                    { 2, new DateTimeOffset(new DateTime(2026, 7, 9, 1, 45, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "WaitingForRider", "Packing", "Preparing", "Paid", 1398.00m, new DateTimeOffset(new DateTime(2026, 7, 9, 2, 5, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "bmacha2015@gmail.com" }
                });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Description", "ImageUrl", "Name", "Price", "QuantityAvailable", "ShortDescription" },
                values: new object[,]
                {
                    { 1, "A balanced medium roast with cocoa notes for daily brewing.", "https://images.unsplash.com/photo-1447933601403-0c6688de566e?auto=format&fit=crop&w=900&q=80", "Artisan Coffee Beans", 349.00m, 100, "Small-batch roasted arabica beans." },
                    { 2, "A six-piece box of layered butter croissants for breakfast or gifting.", "https://images.unsplash.com/photo-1555507036-ab1f4038808a?auto=format&fit=crop&w=900&q=80", "Butter Croissant Box", 429.00m, 50, "Flaky croissants baked fresh." },
                    { 3, "Includes artisan bread, deli cuts, crisp greens, and house dressing.", "https://images.unsplash.com/photo-1528735602780-2552fd46c7af?auto=format&fit=crop&w=900&q=80", "Gourmet Sandwich Kit", 549.00m, 35, "Premium sandwich ingredients ready to assemble." },
                    { 4, "A chilled fruit bowl with strawberries, blueberries, yogurt, and granola.", "https://images.unsplash.com/photo-1490474418585-ba9bad8fd0ea?auto=format&fit=crop&w=900&q=80", "Fresh Berry Bowl", 299.00m, 70, "Seasonal berries with granola." },
                    { 5, "Pasta tray prepared for sharing, finished with parmesan and herbs.", "https://images.unsplash.com/photo-1473093295043-cdd812d0e601?auto=format&fit=crop&w=900&q=80", "Signature Pasta Tray", 699.00m, 25, "Family-size pasta with tomato basil sauce." }
                });

            migrationBuilder.InsertData(
                table: "CartItems",
                columns: new[] { "Id", "CreatedAtUtc", "ProductId", "Quantity", "UpdatedAtUtc", "VisitorEmail" },
                values: new object[,]
                {
                    { 1, new DateTimeOffset(new DateTime(2026, 7, 9, 2, 10, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, 2, new DateTimeOffset(new DateTime(2026, 7, 9, 2, 10, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "bmacha2015@gmail.com" },
                    { 2, new DateTimeOffset(new DateTime(2026, 7, 9, 2, 12, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 4, 1, new DateTimeOffset(new DateTime(2026, 7, 9, 2, 12, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "bmacha2015@gmail.com" }
                });

            migrationBuilder.InsertData(
                table: "Notifications",
                columns: new[] { "Id", "Body", "Channel", "CreatedAtUtc", "IsRead", "OrderId", "RecipientEmail", "SentAtUtc", "Subject" },
                values: new object[,]
                {
                    { 1, "Order #1 for bmacha2015@gmail.com. Status: Delivered; Payment: Paid; Fulfillment: AssignedToRider; Delivery: Delivered. Total: PHP 1277.00.", "Email", new DateTimeOffset(new DateTime(2026, 7, 8, 3, 31, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 1, "bmacha2026@gmail.com", new DateTimeOffset(new DateTime(2026, 7, 8, 3, 31, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "New order #1" },
                    { 2, "Your order #1 was received and is now delivered. Total: PHP 1277.00.", "Email", new DateTimeOffset(new DateTime(2026, 7, 8, 3, 32, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 1, "bmacha2015@gmail.com", new DateTimeOffset(new DateTime(2026, 7, 8, 3, 32, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Order #1 received" },
                    { 3, "Order #1 is delivered. Thank you for your purchase.", "Email", new DateTimeOffset(new DateTime(2026, 7, 8, 5, 15, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, 1, "bmacha2015@gmail.com", new DateTimeOffset(new DateTime(2026, 7, 8, 5, 15, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Order #1 status updated" },
                    { 4, "Order #2 for bmacha2015@gmail.com. Status: Preparing; Payment: Paid; Fulfillment: Packing; Delivery: WaitingForRider. Total: PHP 1398.00.", "Email", new DateTimeOffset(new DateTime(2026, 7, 9, 1, 46, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, 2, "bmacha2026@gmail.com", new DateTimeOffset(new DateTime(2026, 7, 9, 1, 46, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "New order #2" },
                    { 5, "Your order #2 was received and is being prepared. Total: PHP 1398.00.", "Email", new DateTimeOffset(new DateTime(2026, 7, 9, 1, 47, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, 2, "bmacha2015@gmail.com", new DateTimeOffset(new DateTime(2026, 7, 9, 1, 47, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Order #2 received" }
                });

            migrationBuilder.InsertData(
                table: "OrderItems",
                columns: new[] { "Id", "OrderId", "ProductId", "ProductName", "Quantity", "Subtotal", "UnitPrice" },
                values: new object[,]
                {
                    { 1, 1, 2, "Butter Croissant Box", 1, 429.00m, 429.00m },
                    { 2, 1, 3, "Gourmet Sandwich Kit", 1, 549.00m, 549.00m },
                    { 3, 1, 4, "Fresh Berry Bowl", 1, 299.00m, 299.00m },
                    { 4, 2, 5, "Signature Pasta Tray", 2, 1398.00m, 699.00m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_ProductId",
                table: "CartItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_VisitorEmail_ProductId",
                table: "CartItems",
                columns: new[] { "VisitorEmail", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrderId",
                table: "Notifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
