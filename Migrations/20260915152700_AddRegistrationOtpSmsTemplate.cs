using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationOtpSmsTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO notification_templates 
                (template_key, event_type, channel, language, body, provider_template_id, provider_template_name, is_active, created_at, updated_at)
                VALUES 
                ('REGISTRATION_OTP', 'Registration', 'SMS', 'en', 
                 'Your PickNBook Registration OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP with anyone -- Pirnav Software Solutions', 
                 '1777178884304449300', 'REGISTRATION_OTP', 1, NOW(), NOW())
                ON DUPLICATE KEY UPDATE 
                    body = VALUES(body), 
                    provider_template_id = VALUES(provider_template_id), 
                    provider_template_name = VALUES(provider_template_name),
                    is_active = 1,
                    updated_at = NOW();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM notification_templates 
                WHERE template_key = 'REGISTRATION_OTP' AND channel = 'SMS';
            ");
        }
    }
}
