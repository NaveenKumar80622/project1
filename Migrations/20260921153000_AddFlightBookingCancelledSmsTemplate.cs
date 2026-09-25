using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlightBookingCancelledSmsTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO notification_templates (template_key, event_type, channel, language, subject, body, provider_template_id, provider_template_name, is_active, created_at, updated_at)
                VALUES 
                ('FLIGHT_BOOKING_CANCELLED', 'FlightBookingCancelled', 'SMS', 'en', 'PICNBK', 'Pick&Book: Flight booking {Reference} has been cancelled. Cancellation status: {Status}.', '1777178996735449379', 'FLIGHT_BOOKING_CANCELLED', 1, NOW(), NOW()),
                ('FLIGHT_BOOKING_CANCELLED_SMS', 'FlightBookingCancelled', 'SMS', 'en', 'PICNBK', 'Pick&Book: Flight booking {Reference} has been cancelled. Cancellation status: {Status}.', '1777178996735449379', 'FLIGHT_BOOKING_CANCELLED', 1, NOW(), NOW())
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
                WHERE template_key IN ('FLIGHT_BOOKING_CANCELLED', 'FLIGHT_BOOKING_CANCELLED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
