using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCancellationsAndPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationPolicyJson",
                table: "flight_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CancellationPolicyJson",
                table: "bus_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BookingCancellations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BookingReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BookingType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalCustomerPaid = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SupplierAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarkupAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ConvenienceFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SupplierCancellationCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SupplierRefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarkupRefunded = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CouponForfeited = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeeRefunded = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CustomerRefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SrdvChangeRequestId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SrdvStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CashfreeRefundId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailureReason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCancellations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingCancellations");

            migrationBuilder.DropColumn(
                name: "CancellationPolicyJson",
                table: "flight_reservations");

            migrationBuilder.DropColumn(
                name: "CancellationPolicyJson",
                table: "bus_reservations");
        }
    }
}
