using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSsrTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SsrAmountInr",
                table: "flight_reservations",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BaggageJson",
                table: "flight_reservation_passengers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MealJson",
                table: "flight_reservation_passengers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "SsrTotalInr",
                table: "flight_reservation_passengers",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SsrAmountInr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "BaggageJson",
                table: "flight_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "MealJson",
                table: "flight_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "SsrTotalInr",
                table: "flight_reservation_passengers");
        }
    }
}
