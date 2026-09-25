using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBusBookingConfirmedToNewDltTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Bus booking confirmed. Ref {Reference}, Pnr {Pnr}. Boarding: {Boarding} at {Time}.',
                    provider_template_id = '1777178997159302603',
                    provider_template_name = 'newBUS_BOOKING_CONFIRMED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('BUS_BOOKING_CONFIRMED', 'BUS_BOOKING_CONFIRMED_SMS') AND channel = 'SMS';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'PickNBook: Bus booking confirmed. Ref {Reference}, Pnr {Pnr}. Boarding: {Boarding} at {Time}.',
                    provider_template_id = '1777178954051622003',
                    provider_template_name = 'BUS_BOOKING_CONFIRMED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('BUS_BOOKING_CONFIRMED', 'BUS_BOOKING_CONFIRMED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
