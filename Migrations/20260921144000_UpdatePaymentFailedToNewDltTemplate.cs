using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePaymentFailedToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Payment failed for reference {Reference}. Reason: {Reason}.',
                    provider_template_id = '1777178997180999327',
                    provider_template_name = 'newPAYMENT_FAILED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'PAYMENT_FAILED' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Payment failed for reference {Reference}. Reason: {Reason}. Please retry or contact support.',
                    provider_template_id = '1777178903298120394',
                    provider_template_name = 'PAYMENT_FAILED',
                    subject = 'PICKBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'PAYMENT_FAILED' AND channel = 'SMS';
            ");
        }
    }
}
