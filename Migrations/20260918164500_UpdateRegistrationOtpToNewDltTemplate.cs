using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRegistrationOtpToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Your Pick&Book Registration OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP with anyone.',
                    provider_template_id = '1777178972156267667',
                    provider_template_name = 'NEWREGISTRATION_OTP',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'REGISTRATION_OTP' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Your PickNBook Registration OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP with anyone -- Pirnav Software Solutions',
                    provider_template_id = '1777178884304449300',
                    provider_template_name = 'REGISTRATION_OTP',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'REGISTRATION_OTP' AND channel = 'SMS';
            ");
        }
    }
}
