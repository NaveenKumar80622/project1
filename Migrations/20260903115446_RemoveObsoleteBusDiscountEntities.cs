using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveObsoleteBusDiscountEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                SET FOREIGN_KEY_CHECKS = 0;
                ALTER TABLE `bus_reservations` DROP FOREIGN KEY `FK_bus_reservations_buspromotions_AppliedPromotionId`;
                DROP TABLE IF EXISTS `busdiscountconditions`;
                DROP TABLE IF EXISTS `buspromotionconditions`;
                DROP TABLE IF EXISTS `buspromotionusages`;
                DROP TABLE IF EXISTS `bus_discounts`;
                DROP TABLE IF EXISTS `buspromotions`;
                SET FOREIGN_KEY_CHECKS = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bus_discounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EndDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EntryDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsAutoApply = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsExclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinBookingAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Remark = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdateDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bus_discounts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "buspromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountValue = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsAutoApply = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsExclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFirstTimeUserOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxDiscountAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    MaxUsage = table.Column<int>(type: "int", nullable: true),
                    MaxUsagePerUser = table.Column<int>(type: "int", nullable: false),
                    MinBookingAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    PromotionType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceKey = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UsedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buspromotions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "busdiscountconditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusDiscountId = table.Column<int>(type: "int", nullable: false),
                    ConditionOperator = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConditionType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value1 = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value2 = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_busdiscountconditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_busdiscountconditions_bus_discounts_BusDiscountId",
                        column: x => x.BusDiscountId,
                        principalTable: "bus_discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "buspromotionconditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusPromotionId = table.Column<int>(type: "int", nullable: false),
                    ConditionOperator = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConditionType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value1 = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value2 = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buspromotionconditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buspromotionconditions_buspromotions_BusPromotionId",
                        column: x => x.BusPromotionId,
                        principalTable: "buspromotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "buspromotionusages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusPromotionId = table.Column<int>(type: "int", nullable: false),
                    BusReservationId = table.Column<int>(type: "int", nullable: false),
                    BookingStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BookingTotalInr = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DiscountAmountInr = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    FeaturedOfferId = table.Column<int>(type: "int", nullable: true),
                    PromotionCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PromotionType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buspromotionusages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buspromotionusages_bus_reservations_BusReservationId",
                        column: x => x.BusReservationId,
                        principalTable: "bus_reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_buspromotionusages_buspromotions_BusPromotionId",
                        column: x => x.BusPromotionId,
                        principalTable: "buspromotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_busdiscountconditions_BusDiscountId",
                table: "busdiscountconditions",
                column: "BusDiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_buspromotionconditions_BusPromotionId",
                table: "buspromotionconditions",
                column: "BusPromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_buspromotionusages_BusPromotionId",
                table: "buspromotionusages",
                column: "BusPromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_buspromotionusages_BusReservationId",
                table: "buspromotionusages",
                column: "BusReservationId");
        }
    }
}
