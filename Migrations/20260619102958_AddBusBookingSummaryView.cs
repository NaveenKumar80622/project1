using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusBookingSummaryView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW v_BusBookingSummary AS
                SELECT 
                    r.Id,
                    r.BookedAtUtc,
                    r.SeatsBooked,
                    CONCAT(b.FromCity, ' - ', b.ToCity) AS Segment,
                    r.Pnr,
                    r.BookingReference,
                    r.Status,
                    b.OperatorName AS BusOperator,
                    b.BusType,
                    CASE WHEN r.CustomerFareInr > 0 THEN r.CustomerFareInr ELSE r.TotalPriceInr END AS CustomerFareInr,
                    CASE WHEN r.NetFareInr > 0 THEN r.NetFareInr ELSE r.TotalPriceInr END AS NetFareInr,
                    (r.MarkupAmountInr + r.ConvenienceFeeInr) AS ProfitInr,
                    r.DiscountAmountInr,
                    r.ConvenienceFeeInr,
                    r.BaseFareInr,
                    r.MarkupAmountInr,
                    r.TaxableFareInr,
                    r.GstPercent,
                    r.GstAmountInr,
                    b.DepartureTime,
                    b.ArrivalTime
                FROM bus_reservations r
                LEFT JOIN bus_bookings b ON r.BusBookingId = b.Id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_BusBookingSummary;");
        }
    }
}
