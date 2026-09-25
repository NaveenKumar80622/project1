using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixLoginOtpSmsTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET body = 'Dear User, your OTP for login to ${var1}  is ${var2} . This OTP is valid for 10 minutes. Do not share it with anyone - PITSOP', 
                    updated_at = NOW() 
                WHERE template_key = 'LOGIN_OTP' AND channel = 'SMS';
                
                INSERT INTO notification_templates (template_key, event_type, channel, language, body, is_active, created_at, updated_at)
                SELECT 'LOGIN_OTP', 'Login', 'SMS', 'en', 'Dear User, your OTP for login to ${var1}  is ${var2} . This OTP is valid for 10 minutes. Do not share it with anyone - PITSOP', 1, NOW(), NOW()
                WHERE NOT EXISTS (
                    SELECT 1 FROM notification_templates WHERE template_key = 'LOGIN_OTP' AND channel = 'SMS'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET body = 'Dear User, your OTP for login to ShyamAgro is {OtpCode}. This OTP is valid for 10 minutes. Do not share it with anyone', 
                    updated_at = NOW() 
                WHERE template_key = 'LOGIN_OTP' AND channel = 'SMS';
            ");
        }
    }
}
