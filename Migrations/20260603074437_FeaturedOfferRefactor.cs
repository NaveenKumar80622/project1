using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class FeaturedOfferRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP PROCEDURE IF EXISTS RefactorFeaturedOffers;
                CREATE PROCEDURE RefactorFeaturedOffers()
                BEGIN
                    -- 1. Drop foreign key if it exists
                    IF EXISTS (
                        SELECT 1 
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
                        WHERE CONSTRAINT_SCHEMA = DATABASE() 
                          AND TABLE_NAME = 'featuredoffers' 
                          AND CONSTRAINT_NAME = 'FK_feat_offers_bus_promotions_id'
                    ) THEN
                        ALTER TABLE featuredoffers DROP FOREIGN KEY FK_feat_offers_bus_promotions_id;
                    END IF;

                    -- 2. Add columns to featuredoffers if they don't exist
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'DiscountType') THEN
                        ALTER TABLE featuredoffers ADD DiscountType longtext NOT NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'DiscountValue') THEN
                        ALTER TABLE featuredoffers ADD DiscountValue decimal(10,2) NOT NULL DEFAULT 0.0;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'StartDateUtc') THEN
                        ALTER TABLE featuredoffers ADD StartDateUtc datetime(6) NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'EndDateUtc') THEN
                        ALTER TABLE featuredoffers ADD EndDateUtc datetime(6) NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'MaxDiscountAmount') THEN
                        ALTER TABLE featuredoffers ADD MaxDiscountAmount decimal(10,2) NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'MaxUsage') THEN
                        ALTER TABLE featuredoffers ADD MaxUsage int NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'MinBookingAmount') THEN
                        ALTER TABLE featuredoffers ADD MinBookingAmount decimal(10,2) NOT NULL DEFAULT 0.0;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'UsedCount') THEN
                        ALTER TABLE featuredoffers ADD UsedCount int NOT NULL DEFAULT 0;
                    END IF;

                    -- 3. Copy data if PromotionId column still exists (which means we haven't completed the migration copy yet)
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'featuredoffers' AND COLUMN_NAME = 'PromotionId') THEN
                        UPDATE featuredoffers fo
                        INNER JOIN buspromotions bp ON bp.Id = fo.PromotionId
                        SET 
                            fo.DiscountType = bp.DiscountType,
                            fo.DiscountValue = bp.DiscountValue,
                            fo.MaxDiscountAmount = bp.MaxDiscountAmount,
                            fo.MinBookingAmount = bp.MinBookingAmount,
                            fo.StartDateUtc = bp.StartDateUtc,
                            fo.EndDateUtc = bp.EndDateUtc,
                            fo.MaxUsage = bp.MaxUsage,
                            fo.UsedCount = bp.UsedCount;

                        ALTER TABLE featuredoffers DROP COLUMN PromotionId;
                    END IF;

                    -- 4. Add applied reservation columns to bus_reservations if they don't exist
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bus_reservations' AND COLUMN_NAME = 'AppliedFeaturedOfferTitle') THEN
                        ALTER TABLE bus_reservations ADD AppliedFeaturedOfferTitle longtext NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bus_reservations' AND COLUMN_NAME = 'FeaturedOfferDiscountAmount') THEN
                        ALTER TABLE bus_reservations ADD FeaturedOfferDiscountAmount decimal(65,30) NOT NULL DEFAULT 0.0;
                    END IF;

                    -- 5. Create featuredofferusages table if it doesn't exist
                    CREATE TABLE IF NOT EXISTS featuredofferusages (
                        Id int NOT NULL AUTO_INCREMENT,
                        FeaturedOfferId int NOT NULL,
                        UserId varchar(50) CHARACTER SET utf8mb4 NOT NULL,
                        BusReservationId int NOT NULL,
                        DiscountAmount decimal(10,2) NOT NULL,
                        UsedAtUtc datetime(6) NOT NULL,
                        PRIMARY KEY (Id),
                        KEY IX_featuredofferusages_BusReservationId (BusReservationId),
                        KEY IX_featuredofferusages_FeaturedOfferId (FeaturedOfferId),
                        CONSTRAINT FK_featuredofferusages_bus_reservations_BusReservationId FOREIGN KEY (BusReservationId) REFERENCES bus_reservations (Id) ON DELETE CASCADE,
                        CONSTRAINT FK_featuredofferusages_featuredoffers_FeaturedOfferId FOREIGN KEY (FeaturedOfferId) REFERENCES featuredoffers (Id) ON DELETE CASCADE
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                END;
                CALL RefactorFeaturedOffers();
                DROP PROCEDURE RefactorFeaturedOffers;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "featuredofferusages");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "EndDateUtc",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "MaxDiscountAmount",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "MaxUsage",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "MinBookingAmount",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "StartDateUtc",
                table: "featuredoffers");

            migrationBuilder.DropColumn(
                name: "AppliedFeaturedOfferTitle",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "FeaturedOfferDiscountAmount",
                table: "bus_reservations");

            migrationBuilder.DropColumn(
                name: "UsedCount",
                table: "featuredoffers");

            migrationBuilder.AddColumn<int>(
                name: "PromotionId",
                table: "featuredoffers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_featuredoffers_PromotionId",
                table: "featuredoffers",
                column: "PromotionId");

            migrationBuilder.AddForeignKey(
                name: "FK_feat_offers_bus_promotions_id",
                table: "featuredoffers",
                column: "PromotionId",
                principalTable: "buspromotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
