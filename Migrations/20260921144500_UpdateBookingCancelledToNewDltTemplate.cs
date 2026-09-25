using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBookingCancelledToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Booking {Reference} has been cancelled. Cancellation status: {Status}.',
                    provider_template_id = '1777178997200725228',
                    provider_template_name = 'newBOOKING_CANCELLED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'BOOKING_CANCELLED' AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Booking {Reference} has been cancelled. Cancellation status: {Status}.',
                    provider_template_id = '1777178903406742408',
                    provider_template_name = 'BOOKING_CANCELLED',
                    subject = 'PICKBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key = 'BOOKING_CANCELLED' AND channel = 'SMS';
            ");
        }
    }
}
