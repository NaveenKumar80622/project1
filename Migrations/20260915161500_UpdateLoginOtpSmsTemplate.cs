using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateLoginOtpSmsTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Your PickNBook login OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP with anyone.',
                    provider_template_id = '1777178903191272452',
                    provider_template_name = 'LOGIN_OTP',
                    subject = 'PICKBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'LOGIN_OTP' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Dear User, your OTP for login to ${var1}  is ${var2} . This OTP is valid for 10 minutes. Do not share it with anyone - PITSOP',
                    provider_template_id = NULL,
                    provider_template_name = NULL,
                    updated_at = NOW()
                WHERE template_key = 'LOGIN_OTP' AND channel = 'SMS';
            ");
        }
    }
}
