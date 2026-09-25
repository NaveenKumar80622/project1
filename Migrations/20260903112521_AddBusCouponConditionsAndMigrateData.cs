using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusCouponConditionsAndMigrateData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "bus_coupons",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountAmount",
                table: "bus_coupons",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionCategory",
                table: "bus_coupons",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Coupon")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "bus_coupons",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BusCouponId",
                table: "bus_coupon_usages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bus_coupon_conditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusCouponId = table.Column<int>(type: "int", nullable: false),
                    ConditionType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConditionOperator = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value1 = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value2 = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bus_coupon_conditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bus_coupon_conditions_bus_coupons_BusCouponId",
                        column: x => x.BusCouponId,
                        principalTable: "bus_coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_bus_coupons_PromotionCategory",
                table: "bus_coupons",
                column: "PromotionCategory");

            migrationBuilder.CreateIndex(
                name: "IX_bus_coupon_usages_BusCouponId",
                table: "bus_coupon_usages",
                column: "BusCouponId");

            migrationBuilder.CreateIndex(
                name: "IX_bus_coupon_conditions_BusCouponId",
                table: "bus_coupon_conditions",
                column: "BusCouponId");

            migrationBuilder.CreateIndex(
                name: "IX_bus_coupon_conditions_ConditionType",
                table: "bus_coupon_conditions",
                column: "ConditionType");

            migrationBuilder.AddForeignKey(
                name: "FK_bus_coupon_usages_bus_coupons_BusCouponId",
                table: "bus_coupon_usages",
                column: "BusCouponId",
                principalTable: "bus_coupons",
                principalColumn: "Id");

            // =========================================================================
            // DATA MIGRATION: Copy existing Bus Discounts & Conditions into BusCoupon
            // =========================================================================
            migrationBuilder.Sql(@"
                INSERT INTO bus_coupons (
                    Value, CouponType, CouponCode, StartDate, ExpiryDate, UseLimit, UsedCount, 
                    Status, EntryDateUtc, Remark, MaxUsagePerUser, MinBookingAmount, IsExclusive, 
                    IsAutoApply, IsFirstTimeUserOnly, Priority, PromotionCategory, Title, Description, MaxDiscountAmount
                )
                SELECT 
                    d.Value,
                    d.DiscountType,
                    COALESCE(NULLIF(TRIM(d.Code), ''), CONCAT('DISC-', d.Id)),
                    COALESCE(DATE(d.StartDateUtc), '2026-01-01'),
                    COALESCE(DATE(d.EndDateUtc), '2036-12-31'),
                    0,
                    0,
                    d.Status,
                    UTC_TIMESTAMP(),
                    d.Remark,
                    1,
                    COALESCE(d.MinBookingAmount, 0),
                    d.IsExclusive,
                    d.IsAutoApply,
                    0,
                    d.Priority,
                    'Offer',
                    d.Title,
                    d.Description,
                    NULL
                FROM bus_discounts d
                WHERE NOT EXISTS (
                    SELECT 1 FROM bus_coupons c 
                    WHERE c.CouponCode = COALESCE(NULLIF(TRIM(d.Code), ''), CONCAT('DISC-', d.Id))
                );
            ");

            migrationBuilder.Sql(@"
                INSERT INTO bus_coupon_conditions (BusCouponId, ConditionType, ConditionOperator, Value1, Value2)
                SELECT 
                    c.Id,
                    dc.ConditionType,
                    dc.ConditionOperator,
                    dc.Value1,
                    dc.Value2
                FROM busdiscountconditions dc
                JOIN bus_discounts d ON dc.BusDiscountId = d.Id
                JOIN bus_coupons c ON c.CouponCode = COALESCE(NULLIF(TRIM(d.Code), ''), CONCAT('DISC-', d.Id))
                WHERE NOT EXISTS (
                    SELECT 1 FROM bus_coupon_conditions bcc
                    WHERE bcc.BusCouponId = c.Id
                      AND bcc.ConditionType = dc.ConditionType
                      AND bcc.Value1 = dc.Value1
                );
            ");

            migrationBuilder.Sql(@"
                INSERT INTO bus_coupons (
                    Value, CouponType, CouponCode, StartDate, ExpiryDate, UseLimit, UsedCount, 
                    Status, EntryDateUtc, Remark, MaxUsagePerUser, MinBookingAmount, IsExclusive, 
                    IsAutoApply, IsFirstTimeUserOnly, Priority, PromotionCategory, Title, Description, MaxDiscountAmount
                )
                SELECT 
                    fo.DiscountValue,
                    fo.DiscountType,
                    CONCAT('OFFER-', fo.Id),
                    COALESCE(DATE(fo.StartDateUtc), '2026-01-01'),
                    COALESCE(DATE(fo.EndDateUtc), '2036-12-31'),
                    COALESCE(fo.MaxUsage, 0),
                    fo.UsedCount,
                    CASE WHEN fo.IsActive = 1 THEN 'Active' ELSE 'Inactive' END,
                    UTC_TIMESTAMP(),
                    fo.Subtitle,
                    1,
                    COALESCE(fo.MinBookingAmount, 0),
                    1,
                    0,
                    0,
                    fo.DisplayOrder,
                    'Offer',
                    fo.Title,
                    fo.Description,
                    fo.MaxDiscountAmount
                FROM featuredoffers fo
                WHERE fo.BookingType = 'Bus'
                  AND NOT EXISTS (
                    SELECT 1 FROM bus_coupons c WHERE c.CouponCode = CONCAT('OFFER-', fo.Id)
                );
            ");

            migrationBuilder.Sql(@"
                INSERT INTO bus_coupon_conditions (BusCouponId, ConditionType, ConditionOperator, Value1, Value2)
                SELECT 
                    c.Id,
                    foc.ConditionType,
                    'Equals',
                    foc.Value1,
                    foc.Value2
                FROM featuredofferconditions foc
                JOIN featuredoffers fo ON foc.FeaturedOfferId = fo.Id
                JOIN bus_coupons c ON c.CouponCode = CONCAT('OFFER-', fo.Id)
                WHERE fo.BookingType = 'Bus'
                  AND NOT EXISTS (
                    SELECT 1 FROM bus_coupon_conditions bcc
                    WHERE bcc.BusCouponId = c.Id
                      AND bcc.ConditionType = foc.ConditionType
                      AND bcc.Value1 = foc.Value1
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bus_coupon_usages_bus_coupons_BusCouponId",
                table: "bus_coupon_usages");

            migrationBuilder.DropTable(
                name: "bus_coupon_conditions");

            migrationBuilder.DropIndex(
                name: "IX_bus_coupons_PromotionCategory",
                table: "bus_coupons");

            migrationBuilder.DropIndex(
                name: "IX_bus_coupon_usages_BusCouponId",
                table: "bus_coupon_usages");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "bus_coupons");

            migrationBuilder.DropColumn(
                name: "MaxDiscountAmount",
                table: "bus_coupons");

            migrationBuilder.DropColumn(
                name: "PromotionCategory",
                table: "bus_coupons");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "bus_coupons");

            migrationBuilder.DropColumn(
                name: "BusCouponId",
                table: "bus_coupon_usages");
        }
    }
}
