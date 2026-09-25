using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateHotelCitiesSchemaV8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_hotel_cities_RequestType_CityCode",
                table: "hotel_cities");

            migrationBuilder.DropIndex(
                name: "IX_hotel_cities_RequestType_CityName",
                table: "hotel_cities");

            migrationBuilder.AddColumn<long>(
                name: "CityId",
                table: "hotel_cities",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DistrictName",
                table: "hotel_cities",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StateName",
                table: "hotel_cities",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "hotel_cities",
                type: "varchar(750)",
                maxLength: 750,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "hotel_cities",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "CITY")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "HotelCount",
                table: "hotel_cities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_hotel_cities_CityId",
                table: "hotel_cities",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_hotel_cities_FullName",
                table: "hotel_cities",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_hotel_cities_HotelCount",
                table: "hotel_cities",
                column: "HotelCount");

            migrationBuilder.CreateIndex(
                name: "IX_hotel_cities_CountryCode",
                table: "hotel_cities",
                column: "CountryCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_hotel_cities_CityId",
                table: "hotel_cities");

            migrationBuilder.DropIndex(
                name: "IX_hotel_cities_FullName",
                table: "hotel_cities");

            migrationBuilder.DropIndex(
                name: "IX_hotel_cities_HotelCount",
                table: "hotel_cities");

            migrationBuilder.DropIndex(
                name: "IX_hotel_cities_CountryCode",
                table: "hotel_cities");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "hotel_cities");

            migrationBuilder.DropColumn(
                name: "DistrictName",
                table: "hotel_cities");

            migrationBuilder.DropColumn(
                name: "StateName",
                table: "hotel_cities");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "hotel_cities");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "hotel_cities");

            migrationBuilder.DropColumn(
                name: "HotelCount",
                table: "hotel_cities");

            migrationBuilder.CreateIndex(
                name: "IX_hotel_cities_RequestType_CityCode",
                table: "hotel_cities",
                columns: new[] { "RequestType", "CityCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hotel_cities_RequestType_CityName",
                table: "hotel_cities",
                columns: new[] { "RequestType", "CityName" });
        }
    }
}
