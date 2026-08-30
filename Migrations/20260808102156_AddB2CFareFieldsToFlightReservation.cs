using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddB2CFareFieldsToFlightReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "B2CDiscountAmountInr",
                table: "flight_reservations",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "B2CMarkupAmountInr",
                table: "flight_reservations",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "B2CPublishedFareInr",
                table: "flight_reservations",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "B2CDiscountAmountInr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "B2CMarkupAmountInr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "B2CPublishedFareInr",
                table: "flight_reservations");
        }
    }
}
