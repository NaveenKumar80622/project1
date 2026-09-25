using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteAndSegmentsToFlightSearchLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FromCityName",
                table: "flight_search_logs",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RouteSummary",
                table: "flight_search_logs",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SegmentsJson",
                table: "flight_search_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ToCityName",
                table: "flight_search_logs",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromCityName",
                table: "flight_search_logs");

            migrationBuilder.DropColumn(
                name: "RouteSummary",
                table: "flight_search_logs");

            migrationBuilder.DropColumn(
                name: "SegmentsJson",
                table: "flight_search_logs");

            migrationBuilder.DropColumn(
                name: "ToCityName",
                table: "flight_search_logs");
        }
    }
}
