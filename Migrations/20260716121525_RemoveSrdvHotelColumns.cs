using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSrdvHotelColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationPoliciesJson",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvBookingId",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvBookingResponseJson",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "hotel_reservations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationPoliciesJson",
                table: "hotel_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvBookingId",
                table: "hotel_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvBookingResponseJson",
                table: "hotel_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "hotel_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
