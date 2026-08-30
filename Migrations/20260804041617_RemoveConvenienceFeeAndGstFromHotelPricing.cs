using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveConvenienceFeeAndGstFromHotelPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConvenienceFeeType",
                table: "hotel_pricing_settings");

            migrationBuilder.DropColumn(
                name: "ConvenienceFeeValue",
                table: "hotel_pricing_settings");

            migrationBuilder.DropColumn(
                name: "GstPercent",
                table: "hotel_pricing_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConvenienceFeeType",
                table: "hotel_pricing_settings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ConvenienceFeeValue",
                table: "hotel_pricing_settings",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GstPercent",
                table: "hotel_pricing_settings",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
