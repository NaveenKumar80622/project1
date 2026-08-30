using System;

namespace PickNBook.Api.Models
{
    public class BusBookingSummary
    {
        public int Id { get; set; }
        public DateTime BookedAtUtc { get; set; }
        public int SeatsBooked { get; set; }
        public string Segment { get; set; } = string.Empty;
        public string Pnr { get; set; } = string.Empty;
        public string BookingReference { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string BusOperator { get; set; } = string.Empty;
        public string BusType { get; set; } = string.Empty;
        public decimal CustomerFareInr { get; set; }
        public decimal NetFareInr { get; set; }
        public decimal ProfitInr { get; set; }
        public decimal DiscountAmountInr { get; set; }
        public decimal ConvenienceFeeInr { get; set; }
        public decimal BaseFareInr { get; set; }
        public decimal MarkupAmountInr { get; set; }
        public decimal TaxableFareInr { get; set; }
        public decimal GstPercent { get; set; }
        public decimal GstAmountInr { get; set; }
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
    }
}
