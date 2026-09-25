using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerDetailsToPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "payments",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "payments",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "payments",
                type: "varchar(25)",
                maxLength: 25,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "PassengerCount",
                table: "payments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PassengerDetailsJson",
                table: "payments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_payments_CustomerName",
                table: "payments",
                column: "CustomerName");

            migrationBuilder.CreateIndex(
                name: "IX_payments_CustomerPhone",
                table: "payments",
                column: "CustomerPhone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_CustomerName",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_CustomerPhone",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PassengerCount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PassengerDetailsJson",
                table: "payments");
        }
    }
}
