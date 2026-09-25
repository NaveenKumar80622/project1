using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHotelBookingCancelledSmsTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO notification_templates (
                    template_key, 
                    event_type, 
                    channel, 
                    language, 
                    subject, 
                    body, 
                    provider_template_id, 
                    provider_template_name, 
                    is_active, 
                    created_at, 
                    updated_at
                ) VALUES (
                    'HOTEL_BOOKING_CANCELLED', 
                    'HotelBookingCancelled', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Hotel booking ${var1} has been cancelled. Cancellation status: ${var2}.', 
                    '1777178962472996371', 
                    'HOTEL_BOOKING_CANCELLED', 
                    1, 
                    NOW(), 
                    NOW()
                )
                ON DUPLICATE KEY UPDATE
                    body = VALUES(body),
                    provider_template_id = VALUES(provider_template_id),
                    provider_template_name = VALUES(provider_template_name),
                    subject = VALUES(subject),
                    is_active = 1,
                    updated_at = NOW();

                INSERT INTO notification_templates (
                    template_key, 
                    event_type, 
                    channel, 
                    language, 
                    subject, 
                    body, 
                    provider_template_id, 
                    provider_template_name, 
                    is_active, 
                    created_at, 
                    updated_at
                ) VALUES (
                    'HOTEL_BOOKING_CANCELLED_SMS', 
                    'HotelBookingCancelled', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Hotel booking ${var1} has been cancelled. Cancellation status: ${var2}.', 
                    '1777178962472996371', 
                    'HOTEL_BOOKING_CANCELLED', 
                    1, 
                    NOW(), 
                    NOW()
                )
                ON DUPLICATE KEY UPDATE
                    body = VALUES(body),
                    provider_template_id = VALUES(provider_template_id),
                    provider_template_name = VALUES(provider_template_name),
                    subject = VALUES(subject),
                    is_active = 1,
                    updated_at = NOW();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM notification_templates 
                WHERE template_key IN ('HOTEL_BOOKING_CANCELLED', 'HOTEL_BOOKING_CANCELLED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
