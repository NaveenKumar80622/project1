using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePaymentSuccessToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Payment successful. Ref {Reference}. Amount Rs. {Amount}',
                    provider_template_id = '1777178997110621673',
                    provider_template_name = 'newPAYMENT_SUCCESS',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'PAYMENT_SUCCESS' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Payment successful. Ref {Reference}. Amount Rs. {Amount}',
                    provider_template_id = '1777178954083284292',
                    provider_template_name = 'PAYMENT_SUCCESS',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'PAYMENT_SUCCESS' AND channel = 'SMS';
            ");
        }
    }
}
