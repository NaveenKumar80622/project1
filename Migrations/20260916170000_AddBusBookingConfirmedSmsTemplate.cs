using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusBookingConfirmedSmsTemplate : Migration
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
                    'BUS_BOOKING_CONFIRMED', 
                    'BusBookingSuccess', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Bus booking confirmed. Ref {Reference}, Pnr {Pnr}. Boarding: {Boarding} at {Time}.', 
                    '1777178954051622003', 
                    'BUS_BOOKING_CONFIRMED', 
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
                    'BUS_BOOKING_CONFIRMED_SMS', 
                    'BusBookingSuccess', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Bus booking confirmed. Ref {Reference}, Pnr {Pnr}. Boarding: {Boarding} at {Time}.', 
                    '1777178954051622003', 
                    'BUS_BOOKING_CONFIRMED', 
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
                WHERE template_key IN ('BUS_BOOKING_CONFIRMED', 'BUS_BOOKING_CONFIRMED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
