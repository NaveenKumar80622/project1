namespace PickNBook.Api.Models.DTOs
{
    public class SendBusTicketEmailRequest
    {
        // ── Contact ───────────────────────────────────────────
        public string ToEmail { get; set; } = string.Empty;
        public string PassengerName { get; set; } = string.Empty;  // contact name (kept for email body)

        // ── Booking identity ─────────────────────────────────
        public string BookingReference { get; set; } = string.Empty;  // e.g. BS-20260417112019-571
        public string Pnr { get; set; } = string.Empty;               // carrier PNR e.g. OB97X3Y2
        public string OperatorName { get; set; } = string.Empty;       // e.g. "GO TRAVELS"
        public string BusType { get; set; } = string.Empty;            // e.g. "TSRTC", "AC Sleeper"

        // ── Route ─────────────────────────────────────────────
        public string Origin { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public bool IsOvernightArrival { get; set; }
        public int DurationMinutes { get; set; }
        public string BoardingPoint { get; set; } = string.Empty;
        public DateTime BoardingPointTime { get; set; }
        public string ArrivalPoint { get; set; } = string.Empty;
        public DateTime ArrivalPointTime { get; set; }

        // ── Fare breakdown ────────────────────────────────────
        public decimal Price { get; set; }                 // final customer fare (after coupon)
        public decimal GstPercent { get; set; }

        public decimal GstAmount { get; set; }
        public decimal BaseFare { get; set; }              // base fare before coupon
        public decimal NetFare { get; set; }
        public string Currency { get; set; } = "INR";

        // ── Coupon (null if no coupon applied) ────────────────
        public string? CouponCode { get; set; }
        public string? CouponType { get; set; }            // "Percentage" or "Flat"
        public decimal? CouponValue { get; set; }          // e.g. 10 (for 10%) or 100 (flat ₹100)
        public decimal? DiscountAmount { get; set; }
        public string? AppliedPromotionCode { get; set; }

        public string? AppliedPromotionType { get; set; }

        public string? DiscountSource { get; set; }// actual rupee discount

        // ── Passengers ────────────────────────────────────────
        public List<BusPassengerSeatDto> Passengers { get; set; } = new();

        // ── Legacy (kept for cancellation email body) ─────────
        public string SeatNumber { get; set; } = string.Empty;
        public decimal AutoDiscountAmount { get; set; }

        public decimal CouponDiscountAmount { get; set; }
        
        public string? CancellationPoliciesJson { get; set; }
    }

    public class BusPassengerSeatDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string SeatNumber { get; set; } = string.Empty;
    }
}