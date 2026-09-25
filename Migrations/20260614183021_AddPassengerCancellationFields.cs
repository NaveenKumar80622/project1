using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerCancellationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CancellationChargeInr",
                table: "flight_reservations",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmountInr",
                table: "flight_reservations",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "flight_reservation_passengers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "flight_reservation_passengers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "bus_reservation_passengers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "bus_reservation_passengers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationChargeInr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "RefundAmountInr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "flight_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "flight_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "bus_reservation_passengers");
        }
    }
}
