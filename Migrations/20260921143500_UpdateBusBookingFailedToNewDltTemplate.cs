using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBusBookingFailedToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Bus booking could not be completed. Ref {Reference}. Reason: {Reason}.',
                    provider_template_id = '1777178997190921279',
                    provider_template_name = 'newBUS_BOOKING_FAILED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'BUS_BOOKING_FAILED' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Bus booking could not be completed. Ref {Reference}. Reason: {Reason}.',
                    provider_template_id = '1777178903385892614',
                    provider_template_name = 'BUS_BOOKING_FAILED',
                    subject = 'PICKBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'BUS_BOOKING_FAILED' AND channel = 'SMS';
            ");
        }
    }
}
