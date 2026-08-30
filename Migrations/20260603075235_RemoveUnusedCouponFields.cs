using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedCouponFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP PROCEDURE IF EXISTS DropUnusedCouponFields;
                CREATE PROCEDURE DropUnusedCouponFields()
                BEGIN
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'buspromotions' AND COLUMN_NAME = 'TriggerType') THEN
                        ALTER TABLE buspromotions DROP COLUMN TriggerType;
                    END IF;
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bus_coupons' AND COLUMN_NAME = 'PromotionCategory') THEN
                        ALTER TABLE bus_coupons DROP COLUMN PromotionCategory;
                    END IF;
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bus_coupons' AND COLUMN_NAME = 'TriggerType') THEN
                        ALTER TABLE bus_coupons DROP COLUMN TriggerType;
                    END IF;
                END;
                CALL DropUnusedCouponFields();
                DROP PROCEDURE DropUnusedCouponFields;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TriggerType",
                table: "buspromotions",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PromotionCategory",
                table: "bus_coupons",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TriggerType",
                table: "bus_coupons",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
