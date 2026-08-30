using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingColumnsToBlockedSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "busblockedseatprices",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "busblockedseatprices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrandTotal",
                table: "busblockedseatprices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarkupAmount",
                table: "busblockedseatprices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "busblockedseatprices");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "busblockedseatprices");

            migrationBuilder.DropColumn(
                name: "GrandTotal",
                table: "busblockedseatprices");

            migrationBuilder.DropColumn(
                name: "MarkupAmount",
                table: "busblockedseatprices");
        }
    }
}
