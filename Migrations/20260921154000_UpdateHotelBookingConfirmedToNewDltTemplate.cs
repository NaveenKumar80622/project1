using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateHotelBookingConfirmedToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Hotel booking confirmed. Ref {Reference}. Hotel: {Hotel}. Check-in: {CheckIn}. Check-out: {CheckOut}.',
                    provider_template_id = '1777178997026681565',
                    provider_template_name = 'newHOTEL_BOOKING_CONFIRMED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('HOTEL_BOOKING_CONFIRMED', 'HOTEL_BOOKING_CONFIRMED_SMS') AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Hotel booking confirmed. Ref ${var1}. Hotel: ${var2}. Check-in: ${var3}. Check-out: ${var4}.',
                    provider_template_id = '1777178962451868362',
                    provider_template_name = 'HOTEL_BOOKING_CONFIRMED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('HOTEL_BOOKING_CONFIRMED', 'HOTEL_BOOKING_CONFIRMED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
