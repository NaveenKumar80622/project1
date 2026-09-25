using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRefundStatusToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Refund {Status} for booking {Reference}. Refund Ref {RefundRef}. Amount Rs. {Amount}',
                    provider_template_id = '1777178997089847183',
                    provider_template_name = 'newREFUND_STATUS',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'REFUND_STATUS' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Refund {Status} for booking {Reference}. Refund Ref {RefundRef}. Amount Rs. {Amount}',
                    provider_template_id = '1777178954067484925',
                    provider_template_name = 'REFUND_STATUS',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'REFUND_STATUS' AND channel = 'SMS';
            ");
        }
    }
}
