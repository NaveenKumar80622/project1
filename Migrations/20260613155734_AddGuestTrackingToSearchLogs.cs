using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestTrackingToSearchLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "flight_search_logs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserOrGuestId",
                table: "flight_search_logs",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "bus_search_logs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserOrGuestId",
                table: "bus_search_logs",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "flight_search_logs");

            migrationBuilder.DropColumn(
                name: "UserOrGuestId",
                table: "flight_search_logs");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "bus_search_logs");

            migrationBuilder.DropColumn(
                name: "UserOrGuestId",
                table: "bus_search_logs");
        }
    }
}
