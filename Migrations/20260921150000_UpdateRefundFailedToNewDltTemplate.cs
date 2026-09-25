using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRefundFailedToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Refund {Status} for booking {Reference}. Refund Ref {RefundRef}.Please contact support : {SupportUrl}',
                    provider_template_id = '1777178997076571137',
                    provider_template_name = 'newREFUND_FAILED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'REFUND_FAILED' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Refund {Status} for booking {Reference}. Refund Ref {RefundRef}.Please contact support : {SupportUrl}',
                    provider_template_id = '1777178955322468054',
                    provider_template_name = 'REFUND_FAILED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'REFUND_FAILED' AND channel = 'SMS';
            ");
        }
    }
}
