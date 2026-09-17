using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHotelBookingFailedSmsTemplate : Migration
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
                    'HOTEL_BOOKING_FAILED', 
                    'HotelBookingFailed', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Hotel booking could not be completed. Ref ${var1}. Reason: ${var2}.', 
                    '1777178962464726766', 
                    'HOTEL_BOOKING_FAILED', 
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
                    'HOTEL_BOOKING_FAILED_SMS', 
                    'HotelBookingFailed', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Hotel booking could not be completed. Ref ${var1}. Reason: ${var2}.', 
                    '1777178962464726766', 
                    'HOTEL_BOOKING_FAILED', 
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
                WHERE template_key IN ('HOTEL_BOOKING_FAILED', 'HOTEL_BOOKING_FAILED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
