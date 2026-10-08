using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetEmailNotificationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO notification_templates (
                    template_key, event_type, channel, language, subject, body, is_active, created_at, updated_at
                ) VALUES 
                (
                    'PASSWORD_RESET_OTP',
                    'PasswordReset',
                    'Email',
                    'en',
                    'PickNBook: Password Reset Verification Code',
                    '<!DOCTYPE html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""><style>body { font-family: ''Segoe UI'', Arial, sans-serif; background-color: #f8fafc; margin: 0; padding: 24px; color: #334155; }.container { max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; border: 1px solid #e2e8f0; box-shadow: 0 4px 15px rgba(0,0,0,0.05); }.header { background: linear-gradient(135deg, #0284c7 0%, #0369a1 100%); padding: 32px 24px; text-align: center; color: #ffffff; }.header h1 { margin: 0; font-size: 26px; font-weight: 700; letter-spacing: 0.5px; }.header p { margin: 6px 0 0; font-size: 14px; opacity: 0.9; }.content { padding: 32px 28px; line-height: 1.6; font-size: 15px; }.greeting { font-size: 17px; font-weight: 600; margin-bottom: 14px; color: #0f172a; }.otp-box { background: #f0f9ff; border: 2px dashed #0284c7; border-radius: 10px; padding: 22px; text-align: center; margin: 26px 0; }.otp-label { font-size: 12px; text-transform: uppercase; letter-spacing: 1.5px; font-weight: 700; color: #0369a1; }.otp-code { font-size: 34px; font-weight: 800; letter-spacing: 8px; color: #0284c7; margin: 10px 0; font-family: Consolas, monospace; }.expiry { font-size: 13px; color: #64748b; font-weight: 600; }.notice { background: #fffbeb; border-left: 4px solid #f59e0b; padding: 14px 16px; margin: 22px 0; font-size: 13px; color: #92400e; border-radius: 4px; line-height: 1.5; }.footer { padding: 22px; text-align: center; font-size: 12px; color: #94a3b8; background-color: #f8fafc; border-top: 1px solid #f1f5f9; line-height: 1.5; }</style></head><body><div class=""container""><div class=""header""><h1>Pick&amp;book</h1><p>Password Reset Verification</p></div><div class=""content""><div class=""greeting"">Hello {Name},</div><p>We received a request to reset your Pick&amp;book account password. Use the verification code below to complete the process:</p><div class=""otp-box""><div class=""otp-label"">Verification Code</div><div class=""otp-code"">{OtpCode}</div><div class=""expiry"">This code expires in {ExpiryMinutes} minutes</div></div><div class=""notice""><strong>Security Reminder:</strong> Never share this code with anyone. Pick&amp;book representatives will never ask for your verification code. If you did not request this password reset, please ignore this email or review your account security.</div></div><div class=""footer"">&copy; 2026 Pick&amp;book. All rights reserved.<br>This is an automated notification. Please do not reply directly.</div></div></body></html>',
                    1,
                    NOW(),
                    NOW()
                ),
                (
                    'ADMIN_PASSWORD_RESET_OTP',
                    'AdminPasswordReset',
                    'Email',
                    'en',
                    'PickNBook Admin Portal: Password Reset Code',
                    '<!DOCTYPE html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""><style>body { font-family: ''Segoe UI'', Arial, sans-serif; background-color: #0f172a; margin: 0; padding: 24px; color: #334155; }.container { max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; border: 1px solid #334155; box-shadow: 0 6px 20px rgba(0,0,0,0.15); }.header { background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); padding: 32px 24px; text-align: center; color: #ffffff; border-bottom: 4px solid #ef4444; }.header h1 { margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; color: #f8fafc; }.header p { margin: 6px 0 0; font-size: 13px; color: #ef4444; font-weight: 700; letter-spacing: 1px; }.content { padding: 32px 28px; line-height: 1.6; font-size: 15px; }.greeting { font-size: 17px; font-weight: 700; color: #0f172a; margin-bottom: 10px; }.admin-badge { display: inline-block; background: #fee2e2; color: #991b1b; padding: 3px 10px; border-radius: 4px; font-size: 11px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 16px; }.otp-box { background: #f8fafc; border: 2px solid #0f172a; border-radius: 8px; padding: 22px; text-align: center; margin: 26px 0; }.otp-label { font-size: 11px; text-transform: uppercase; letter-spacing: 1.5px; color: #64748b; font-weight: 700; }.otp-code { font-size: 36px; font-weight: 900; letter-spacing: 8px; color: #0f172a; margin: 10px 0; font-family: Consolas, monospace; }.expiry { font-size: 13px; color: #64748b; font-weight: 600; }.alert-danger { background: #fef2f2; border: 1px solid #fee2e2; border-left: 4px solid #ef4444; padding: 14px 16px; margin: 22px 0; font-size: 13px; color: #991b1b; border-radius: 4px; line-height: 1.5; }.footer { padding: 20px; text-align: center; font-size: 11px; color: #64748b; background-color: #f1f5f9; border-top: 1px solid #e2e8f0; line-height: 1.5; }</style></head><body><div class=""container""><div class=""header""><h1>Pick&amp;book &bull; Admin Portal</h1><p>HIGH-PRIVILEGE SECURITY ACTION</p></div><div class=""content""><div class=""greeting"">Hello {Name},</div><span class=""admin-badge"">Administrator Authentication</span><p>An administrator password reset was requested for your account. Please use the authorization code below:</p><div class=""otp-box""><div class=""otp-label"">Admin Authorization Code</div><div class=""otp-code"">{OtpCode}</div><div class=""expiry"">Valid for {ExpiryMinutes} minutes</div></div><div class=""alert-danger""><strong>CRITICAL SECURITY NOTICE:</strong> This verification code grants administrator rights to the Pick&amp;book platform. If you did not initiate this request, notify the security operations team immediately.</div></div><div class=""footer"">Confidential &bull; Pick&amp;book System Administration Security Dispatch<br>This is an automated administrative notification.</div></div></body></html>',
                    1,
                    NOW(),
                    NOW()
                ),
                (
                    'PASSWORD_RESET_SUCCESS',
                    'PasswordResetSuccess',
                    'Email',
                    'en',
                    'Security Alert: Your PickNBook Password Was Changed',
                    '<!DOCTYPE html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""><style>body { font-family: ''Segoe UI'', Arial, sans-serif; background-color: #f8fafc; margin: 0; padding: 24px; color: #334155; }.container { max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; border: 1px solid #e2e8f0; box-shadow: 0 4px 15px rgba(0,0,0,0.05); }.header { background: linear-gradient(135deg, #059669 0%, #047857 100%); padding: 32px 24px; text-align: center; color: #ffffff; }.header h1 { margin: 0; font-size: 26px; font-weight: 700; letter-spacing: 0.5px; }.header p { margin: 6px 0 0; font-size: 14px; opacity: 0.9; }.content { padding: 32px 28px; line-height: 1.6; font-size: 15px; }.greeting { font-size: 17px; font-weight: 600; margin-bottom: 14px; color: #0f172a; }.success-box { background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; padding: 18px 20px; margin: 22px 0; color: #166534; font-size: 14px; }.notice { background: #fef2f2; border-left: 4px solid #ef4444; padding: 14px 16px; margin: 22px 0; font-size: 13px; color: #991b1b; border-radius: 4px; line-height: 1.5; }.footer { padding: 22px; text-align: center; font-size: 12px; color: #94a3b8; background-color: #f8fafc; border-top: 1px solid #f1f5f9; line-height: 1.5; }</style></head><body><div class=""container""><div class=""header""><h1>Pick&amp;book</h1><p>Password Update Confirmation</p></div><div class=""content""><div class=""greeting"">Hello {Name},</div><p>Your Pick&amp;book account password was successfully updated.</p><div class=""success-box""><strong>Timestamp:</strong> {Timestamp}<br><strong>Status:</strong> Password successfully changed</div><div class=""notice""><strong>Did not make this change?</strong> If you did not authorize this change, please contact Pick&amp;book support immediately to secure your account.</div></div><div class=""footer"">&copy; 2026 Pick&amp;book. All rights reserved.<br>This is an automated notification. Please do not reply directly.</div></div></body></html>',
                    1,
                    NOW(),
                    NOW()
                ),
                (
                    'ADMIN_PASSWORD_RESET_SUCCESS',
                    'AdminPasswordResetSuccess',
                    'Email',
                    'en',
                    'CRITICAL: PickNBook Administrator Password Changed',
                    '<!DOCTYPE html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""><style>body { font-family: ''Segoe UI'', Arial, sans-serif; background-color: #0f172a; margin: 0; padding: 24px; color: #334155; }.container { max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; border: 1px solid #334155; box-shadow: 0 6px 20px rgba(0,0,0,0.15); }.header { background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); padding: 32px 24px; text-align: center; color: #ffffff; border-bottom: 4px solid #ef4444; }.header h1 { margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; color: #f8fafc; }.header p { margin: 6px 0 0; font-size: 13px; color: #ef4444; font-weight: 700; letter-spacing: 1px; }.content { padding: 32px 28px; line-height: 1.6; font-size: 15px; }.greeting { font-size: 17px; font-weight: 700; color: #0f172a; margin-bottom: 10px; }.admin-badge { display: inline-block; background: #fee2e2; color: #991b1b; padding: 3px 10px; border-radius: 4px; font-size: 11px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 16px; }.success-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 18px 20px; margin: 22px 0; color: #0f172a; font-size: 14px; }.alert-danger { background: #fef2f2; border: 1px solid #fee2e2; border-left: 4px solid #ef4444; padding: 14px 16px; margin: 22px 0; font-size: 13px; color: #991b1b; border-radius: 4px; line-height: 1.5; }.footer { padding: 20px; text-align: center; font-size: 11px; color: #64748b; background-color: #f1f5f9; border-top: 1px solid #e2e8f0; line-height: 1.5; }</style></head><body><div class=""container""><div class=""header""><h1>Pick&amp;book &bull; Admin Portal</h1><p>HIGH-PRIVILEGE SECURITY ACTION</p></div><div class=""content""><div class=""greeting"">Hello {Name},</div><span class=""admin-badge"">Administrator Security Notice</span><p>The password for your Pick&amp;book administrator account was changed successfully.</p><div class=""success-box""><strong>Timestamp:</strong> {Timestamp}<br><strong>Account:</strong> Administrator Credentials Updated</div><div class=""alert-danger""><strong>SECURITY ALERT:</strong> If you did not perform this password change, your administrator account may be compromised. Lock your account and alert system security immediately.</div></div><div class=""footer"">Confidential &bull; Pick&amp;book System Administration Security Dispatch<br>This is an automated administrative notification.</div></div></body></html>',
                    1,
                    NOW(),
                    NOW()
                )
                ON DUPLICATE KEY UPDATE
                    body = VALUES(body),
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
                WHERE template_key IN ('PASSWORD_RESET_OTP', 'ADMIN_PASSWORD_RESET_OTP', 'PASSWORD_RESET_SUCCESS', 'ADMIN_PASSWORD_RESET_SUCCESS')
                  AND channel = 'Email';
            ");
        }
    }
}
