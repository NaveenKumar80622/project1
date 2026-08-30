using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class SrdvApiIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<string>(
                name: "SrdvBookingId",
                table: "flight_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvPnr",
                table: "flight_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvTicketResponseJson",
                table: "flight_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsLcc",
                table: "flight_bookings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResultIndex",
                table: "flight_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SegmentsJson",
                table: "flight_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SrdvIndex",
                table: "flight_bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SrdvType",
                table: "flight_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "flight_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvBookingId",
                table: "bus_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvBookingResponseJson",
                table: "bus_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SrdvTicketNo",
                table: "bus_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OperatorId",
                table: "bus_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ResultIndex",
                table: "bus_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RouteId",
                table: "bus_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SrdvIndex",
                table: "bus_bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "bus_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SrdvBookingId",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvBookingResponseJson",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvBookingId",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvPnr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvTicketResponseJson",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "IsLcc",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "ResultIndex",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "SegmentsJson",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "SrdvIndex",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "SrdvType",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "SrdvBookingId",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvBookingResponseJson",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "SrdvTicketNo",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "bus_bookings");

            migrationBuilder.DropColumn(
                name: "ResultIndex",
                table: "bus_bookings");

            migrationBuilder.DropColumn(
                name: "RouteId",
                table: "bus_bookings");

            migrationBuilder.DropColumn(
                name: "SrdvIndex",
                table: "bus_bookings");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "bus_bookings");
        }
    }
}
