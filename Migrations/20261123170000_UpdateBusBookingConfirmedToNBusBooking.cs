using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBusBookingConfirmedToNBusBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE notification_templates 
                SET 
                    body = 'Pick&Book: Bus booking confirmed. Ref ${var1}. Boarding: ${var2} at ${var3}.',
                    provider_template_id = '1777179542722640556',
                    provider_template_name = 'NBusBooking',
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
                    body = 'Pick&Book: Bus booking confirmed. Ref {Reference}, Pnr {Pnr}. Boarding: {Boarding} at {Time}.',
                    provider_template_id = '1777178997159302603',
                    provider_template_name = 'newBUS_BOOKING_CONFIRMED',
                    subject = 'PICNBK',
                    is_active = 1,
                    updated_at = NOW()
                WHERE template_key IN ('BUS_BOOKING_CONFIRMED', 'BUS_BOOKING_CONFIRMED_SMS') AND channel = 'SMS';
            ");
        }
    }
}
