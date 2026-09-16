using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSuccessSmsTemplate : Migration
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
                    'PAYMENT_SUCCESS', 
                    'PaymentSuccess', 
                    'SMS', 
                    'en', 
                    'PICNBK', 
                    'PickNBook: Payment successful. Ref {Reference}. Amount Rs. {Amount}', 
                    '1777178954083284292', 
                    'PAYMENT_SUCCESS', 
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
                WHERE template_key = 'PAYMENT_SUCCESS' AND channel = 'SMS';
            ");
        }
    }
}
