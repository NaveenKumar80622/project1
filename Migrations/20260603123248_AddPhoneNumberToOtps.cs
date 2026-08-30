using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneNumberToOtps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "otps",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "otps");
        }
    }
}
