using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateHotelBookingFailedToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Hotel booking could not be completed. Ref {Reference}. Reason: {Reason}.',
                    provider_template_id = '1777178997037555745',
                    provider_template_name = 'newHOTEL_BOOKING_FAILED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('HOTEL_BOOKING_FAILED', 'HOTEL_BOOKING_FAILED_SMS') AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Hotel booking could not be completed. Ref ${var1}. Reason: ${var2}.',
                    provider_template_id = '1777178962464726766',
                    provider_template_name = 'HOTEL_BOOKING_FAILED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('HOTEL_BOOKING_FAILED', 'HOTEL_BOOKING_FAILED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
