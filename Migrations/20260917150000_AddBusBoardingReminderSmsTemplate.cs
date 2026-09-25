using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusBoardingReminderSmsTemplate : Migration
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
                    'BUS_BOARDING_REMINDER', 
                    'BusBoardingReminder', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook reminder: Your bus PNR ${var1} departs on ${var2} at ${var3}. Boarding: ${var4}.', 
                    '1777178962432512946', 
                    'BUS_BOARDING_REMINDER', 
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
                    'BUS_BOARDING_REMINDER_SMS', 
                    'BusBoardingReminder', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook reminder: Your bus PNR ${var1} departs on ${var2} at ${var3}. Boarding: ${var4}.', 
                    '1777178962432512946', 
                    'BUS_BOARDING_REMINDER', 
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
                WHERE template_key IN ('BUS_BOARDING_REMINDER', 'BUS_BOARDING_REMINDER_SMS') AND channel = 'SMS';
            ");
        }
    }
}
