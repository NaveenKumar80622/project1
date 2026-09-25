using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateHotelBookingCancelledToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Hotel booking {Reference} has been cancelled. Cancellation status: {Status}.',
                    provider_template_id = '1777178997046597829',
                    provider_template_name = 'newHOTEL_BOOKING_CANCELLED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('HOTEL_BOOKING_CANCELLED', 'HOTEL_BOOKING_CANCELLED_SMS') AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Hotel booking ${var1} has been cancelled. Cancellation status: ${var2}.',
                    provider_template_id = '1777178962472996371',
                    provider_template_name = 'HOTEL_BOOKING_CANCELLED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('HOTEL_BOOKING_CANCELLED', 'HOTEL_BOOKING_CANCELLED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
