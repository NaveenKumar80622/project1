using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHotelCoupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "hotel_reservations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "CouponDiscount",
                table: "hotel_reservations",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "hotel_coupon_usages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    HotelReservationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CouponCode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    CouponType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CouponValue = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    BookingStatus = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hotel_coupon_usages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hotel_coupon_usages_hotel_reservations_HotelReservationId",
                        column: x => x.HotelReservationId,
                        principalTable: "hotel_reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "hotel_coupons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CouponCode = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CouponType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MinBookingAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MaxDiscountAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UseLimit = table.Column<int>(type: "int", nullable: false),
                    UsedCount = table.Column<int>(type: "int", nullable: false),
                    MaxUsagePerUser = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsFirstTimeUserOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EntryDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Remark = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hotel_coupons", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_hotel_coupon_usages_HotelReservationId",
                table: "hotel_coupon_usages",
                column: "HotelReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_hotel_coupons_CouponCode",
                table: "hotel_coupons",
                column: "CouponCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hotel_coupon_usages");

            migrationBuilder.DropTable(
                name: "hotel_coupons");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "hotel_reservations");

            migrationBuilder.DropColumn(
                name: "CouponDiscount",
                table: "hotel_reservations");
        }
    }
}
