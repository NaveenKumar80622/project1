using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyAirportsAndEnrichFlightAirports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add CityName column and index to flight_airports
            migrationBuilder.AddColumn<string>(
                name: "CityName",
                table: "flight_airports",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_flight_airports_CityName",
                table: "flight_airports",
                column: "CityName");

            // 2. Drop legacy airports table
            migrationBuilder.DropTable(
                name: "airports");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flight_airports_CityName",
                table: "flight_airports");

            migrationBuilder.DropColumn(
                name: "CityName",
                table: "flight_airports");
        }
    }
}
