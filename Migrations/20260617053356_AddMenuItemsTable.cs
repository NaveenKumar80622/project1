using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_items",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Slug = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayTitle = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Module = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Location = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_items", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "menu_items",
                columns: new[] { "Id", "CreatedAtUtc", "DisplayTitle", "Location", "Module", "Name", "Order", "Slug", "Status", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Support", "header", "B2C", "Support", 2, "support", "active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Home", "header", "B2C", "Home", 1, "home", "active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Policies", "footer", "B2C", "Policies", 3, "policies", "active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Quick Links", "footer", "B2C", "Quick Links", 2, "quick-links", "active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Services", "footer", "B2C", "Services", 1, "services", "active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_Module_Location_Slug",
                table: "menu_items",
                columns: new[] { "Module", "Location", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_items");
        }
    }
}
