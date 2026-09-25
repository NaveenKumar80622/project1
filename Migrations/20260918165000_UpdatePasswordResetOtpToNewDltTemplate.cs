using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePasswordResetOtpToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Your Pick&Book password reset OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP.',
                    provider_template_id = '1777178972177543618',
                    provider_template_name = 'NewPassword Reset',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'PASSWORD_RESET_OTP' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Your PickNBook password reset OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP.',
                    provider_template_id = '1777178886723752988',
                    provider_template_name = 'Password Reset',
                    subject = 'PICKBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'PASSWORD_RESET_OTP' AND channel = 'SMS';
            ");
        }
    }
}
