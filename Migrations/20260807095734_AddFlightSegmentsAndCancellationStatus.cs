using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlightSegmentsAndCancellationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReturnPnr",
                table: "flight_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "flight_reservation_passengers",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Booked")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CancelledPassengersJson",
                table: "flight_cancellation_requests",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CancelledSectorsJson",
                table: "flight_cancellation_requests",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsPartialCancellation",
                table: "flight_cancellation_requests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "flight_reservation_segments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FlightReservationId = table.Column<int>(type: "int", nullable: false),
                    SegmentIndicator = table.Column<int>(type: "int", nullable: false),
                    TripIndicator = table.Column<int>(type: "int", nullable: false),
                    Airline = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FlightNumber = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromCity = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ToCity = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DepartureTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ArrivalTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false),
                    Baggage = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CabinBaggage = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Pnr = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, defaultValue: "Booked")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_reservation_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_reservation_segments_flight_reservations_FlightReserv~",
                        column: x => x.FlightReservationId,
                        principalTable: "flight_reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_busblockedseatprices_TraceId",
                table: "busblockedseatprices",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_reservation_segments_FlightReservationId",
                table: "flight_reservation_segments",
                column: "FlightReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_reservation_segments");

            migrationBuilder.DropIndex(
                name: "IX_busblockedseatprices_TraceId",
                table: "busblockedseatprices");

            migrationBuilder.DropColumn(
                name: "ReturnPnr",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "flight_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "CancelledPassengersJson",
                table: "flight_cancellation_requests");

            migrationBuilder.DropColumn(
                name: "CancelledSectorsJson",
                table: "flight_cancellation_requests");

            migrationBuilder.DropColumn(
                name: "IsPartialCancellation",
                table: "flight_cancellation_requests");
        }
    }
}
