using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class BusReservation_Passengers_V9_Columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // All DDL in this migration was already committed to the live DB across
            // several partial migration attempts (MySQL has no transactional DDL).
            // The Up() is intentionally empty so EF Core records this migration in
            // __EFMigrationsHistory without re-applying any already-existing changes.
            //
            // Columns confirmed already present in DB:
            //   bus_reservations:          FinancialStatus, ProviderCancelId, SupplierCancelId, TraceId
            //   bus_reservation_passengers: LeadPassenger, PublishedFareInr, GstAmountInr, TaxInr,
            //                               OfferedFareInr, GstRate, FirstName, LastName, Title,
            //                               SeatIndex, IsUpper (+ precision fixes on SeatType, BaseFareInr)
            //   bus_bookings:              SrdvIndex widened to bigint
            //   BookingCancellations:      CancellationType, ProviderCancelId, RefundStatus,
            //                               SeatNamesJson, SupplierCancelId, TraceId
            //   bus_reservations index:    IX_bus_reservations_UserId_TraceId (unique)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropIndex(
                name: "IX_bus_reservations_UserId_TraceId",
                table: "bus_reservations");

            // bus_cities PK on CityId was already in the DB before this migration.
            // Down() restores the PK using raw SQL to avoid Pomelo's auto-increment logic.
            migrationBuilder.Sql("ALTER TABLE `bus_cities` DROP PRIMARY KEY;");
            migrationBuilder.Sql("ALTER TABLE `bus_cities` ADD CONSTRAINT `PK_bus_cities` PRIMARY KEY (`CityId`);");

            // Use raw SQL to drop indexes only if they exist (they may not in all environments)
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_bus_cities_ParentCityId` ON `bus_cities`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_bus_cities_Type` ON `bus_cities`;");

            migrationBuilder.DropColumn(
                name: "FinancialStatus",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "ProviderCancelId",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "SupplierCancelId",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "GstAmountInr",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "GstRate",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "IsUpper",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "LeadPassenger",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "OfferedFareInr",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "PublishedFareInr",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "SeatIndex",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "TaxInr",
                table: "bus_reservation_passengers");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "bus_reservation_passengers");

            // bus_cities columns CityId/DistrictName/ParentCityId/StateCode/Timezone/Type
            // pre-existed this migration - do not drop them in Down().

            migrationBuilder.DropColumn(
                name: "CancellationType",
                table: "BookingCancellations");

            migrationBuilder.DropColumn(
                name: "ProviderCancelId",
                table: "BookingCancellations");

            migrationBuilder.DropColumn(
                name: "RefundStatus",
                table: "BookingCancellations");

            migrationBuilder.DropColumn(
                name: "SeatNamesJson",
                table: "BookingCancellations");

            migrationBuilder.DropColumn(
                name: "SupplierCancelId",
                table: "BookingCancellations");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "BookingCancellations");

            migrationBuilder.AlterColumn<string>(
                name: "SeatType",
                table: "bus_reservation_passengers",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<decimal>(
                name: "BaseFareInr",
                table: "bus_reservation_passengers",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "StateName",
                table: "bus_cities",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "CountryName",
                table: "bus_cities",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "bus_cities",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(10)",
                oldMaxLength: 10)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // Do not attempt to add Id column or switch PK to Id - bus_cities uses CityId as PK.

            migrationBuilder.AlterColumn<int>(
                name: "SrdvIndex",
                table: "bus_bookings",
                type: "int",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
