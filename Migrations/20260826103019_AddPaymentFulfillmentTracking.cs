using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentFulfillmentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookingReferenceId",
                table: "payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FulfillmentStatus",
                table: "payments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingReferenceId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "payments");
        }
    }
}
