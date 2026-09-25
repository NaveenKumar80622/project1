using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsFirstTimeUserOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFirstTimeUserOnly",
                table: "flight_coupons",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFirstTimeUserOnly",
                table: "buspromotions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFirstTimeUserOnly",
                table: "bus_coupons",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFirstTimeUserOnly",
                table: "flight_coupons");

            migrationBuilder.DropColumn(
                name: "IsFirstTimeUserOnly",
                table: "buspromotions");

            migrationBuilder.DropColumn(
                name: "IsFirstTimeUserOnly",
                table: "bus_coupons");
        }
    }
}
