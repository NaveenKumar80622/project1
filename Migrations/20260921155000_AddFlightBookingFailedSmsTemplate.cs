using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlightBookingFailedSmsTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO notification_templates (template_key, event_type, channel, language, subject, body, provider_template_id, provider_template_name, is_active, created_at, updated_at)
                VALUES 
                ('FLIGHT_BOOKING_FAILED', 'FlightBookingFailed', 'SMS', 'en', 'PICNBK', 'Pick&Book: Flight booking could not be completed. Ref: {Reference}. Reason: {Reason}.', '1777178996725418622', 'FLIGHT_BOOKING_FAILED', 1, NOW(), NOW()),
                ('FLIGHT_BOOKING_FAILED_SMS', 'FlightBookingFailed', 'SMS', 'en', 'PICNBK', 'Pick&Book: Flight booking could not be completed. Ref: {Reference}. Reason: {Reason}.', '1777178996725418622', 'FLIGHT_BOOKING_FAILED', 1, NOW(), NOW())
                ON DUPLICATE KEY UPDATE 
                    body = VALUES(body),
                    provider_template_id = VALUES(provider_template_id),
                    provider_template_name = VALUES(provider_template_name),
                    subject = VALUES(subject),
                    is_active = VALUES(is_active),
                    updated_at = NOW();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM notification_templates 
                WHERE template_key IN ('FLIGHT_BOOKING_FAILED', 'FLIGHT_BOOKING_FAILED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
