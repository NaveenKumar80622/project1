using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOfferCodeFromFeaturedOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP PROCEDURE IF EXISTS RemoveOfferCodeField;
                CREATE PROCEDURE RemoveOfferCodeField()
                BEGIN
                    IF EXISTS (
                        SELECT 1 
                        FROM INFORMATION_SCHEMA.STATISTICS 
                        WHERE TABLE_SCHEMA = DATABASE() 
                          AND TABLE_NAME = 'featuredoffers' 
                          AND INDEX_NAME = 'IX_FeaturedOffers_OfferCode'
                    ) THEN
                        ALTER TABLE featuredoffers DROP INDEX IX_FeaturedOffers_OfferCode;
                    END IF;

                    IF EXISTS (
                        SELECT 1 
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_SCHEMA = DATABASE() 
                          AND TABLE_NAME = 'featuredoffers' 
                          AND COLUMN_NAME = 'OfferCode'
                    ) THEN
                        ALTER TABLE featuredoffers DROP COLUMN OfferCode;
                    END IF;
                END;
                CALL RemoveOfferCodeField();
                DROP PROCEDURE RemoveOfferCodeField;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
