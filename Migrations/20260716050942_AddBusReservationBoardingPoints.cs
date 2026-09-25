using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusReservationBoardingPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardingPointName",
                table: "bus_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "BoardingPointTime",
                table: "bus_reservations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DroppingPointName",
                table: "bus_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DroppingPointTime",
                table: "bus_reservations",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardingPointName",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "BoardingPointTime",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "DroppingPointName",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "DroppingPointTime",
                table: "bus_reservations");
        }
    }
}
