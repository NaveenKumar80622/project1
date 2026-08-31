namespace PickNBook.Api.Models
{
    public class FlightReservation
    {
        public int Id { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public string Pnr { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        // Itinerary Fields
        public string FlightNumber { get; set; } = string.Empty;
        public string Airline { get; set; } = string.Empty;
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string? TraceId { get; set; }
        public string? ResultIndex { get; set; }
        public string? SegmentsJson { get; set; }
        public string? FareType { get; set; }
        public string? ReturnPnr { get; set; }
        public ICollection<FlightReservationSegment> Segments { get; set; } = new List<FlightReservationSegment>();

        // Cancellation Policy Fields
        public bool NonRefundable { get; set; }
        public string? CancellationCharges { get; set; }
        public string? FareRulesJson { get; set; }
        public string? CancellationPolicyJson { get; set; }
        public string? PartialSegmentCancellation { get; set; }

        public string PassengerName { get; set; } = string.Empty;
        public string PassengerPhone { get; set; } = string.Empty;
        public string? PassengerEmail { get; set; }
        public string TravelClass { get; set; } = "Economy";
        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }
        public int SeatsBooked { get; set; }
        public decimal TotalPriceInr { get; set; }
        public decimal CustomerFareInr { get; set; }
        public decimal NetFareInr { get; set; }
        public decimal DiscountAmountInr { get; set; }
        public string? CouponCode { get; set; }
        public string Status { get; set; } = "Booked";
        public DateTime BookedAtUtc { get; set; }
        public DateTime? CancelledAtUtc { get; set; }
        public string? CancellationReason { get; set; }
        public decimal? CancellationChargeInr { get; set; }
        public decimal? RefundAmountInr { get; set; }

        public decimal SupplierBaseFare { get; set; }
        public decimal SupplierTaxAmount { get; set; }
        public decimal SupplierTotalFare { get; set; }
        public decimal MarkupAmount { get; set; }
        public decimal B2CPublishedFareInr { get; set; }
        public decimal B2CMarkupAmountInr { get; set; }
        public decimal B2CDiscountAmountInr { get; set; }
        public int? PromotionId { get; set; }
        public string? PromotionName { get; set; }
        public decimal PromotionDiscount { get; set; }
        public int? CouponId { get; set; }
        public decimal CouponDiscount { get; set; }
        public decimal FinalAmount { get; set; }
        public string? PricingSnapshotJson { get; set; }
        public decimal SsrAmountInr { get; set; }

        // SRDV Booking Response Fields
        public bool SSRDenied { get; set; }
        public string? SSRMessage { get; set; }
        public string? SrdvBookingId { get; set; }
        public string? SrdvPnr { get; set; }
        public string? SrdvTicketResponseJson { get; set; }

        // V8 Additional Tracking
        public string? GdsPnr { get; set; }
        public string? TicketStatus { get; set; }
        public string? SrdvChangeRequestId { get; set; }
        public bool IsLcc { get; set; }
        public string? SrdvType { get; set; }
        public string? SrdvIndex { get; set; }
        public string? SrdvCallbackResponseJson { get; set; }
        public DateTime? CallbackReceivedAtUtc { get; set; }
    }
}
