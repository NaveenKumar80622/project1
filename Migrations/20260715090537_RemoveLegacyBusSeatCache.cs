using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyBusSeatCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bus_seats");

            migrationBuilder.DropColumn(
                name: "BoardingPointsJson",
                table: "bus_bookings");

            migrationBuilder.DropColumn(
                name: "DroppingPointsJson",
                table: "bus_bookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardingPointsJson",
                table: "bus_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DroppingPointsJson",
                table: "bus_bookings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "bus_seats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusBookingId = table.Column<int>(type: "int", nullable: false),
                    IsBooked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SeatCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeatType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bus_seats", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_bus_seats_BusBookingId_IsBooked",
                table: "bus_seats",
                columns: new[] { "BusBookingId", "IsBooked" });

            migrationBuilder.CreateIndex(
                name: "IX_bus_seats_BusBookingId_SeatCode",
                table: "bus_seats",
                columns: new[] { "BusBookingId", "SeatCode" },
                unique: true);
        }
    }
}
