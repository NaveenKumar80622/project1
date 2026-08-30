namespace PickNBook.Api.Models
{
    public class BusCouponUsage
    {
        public int Id { get; set; }
        public string UserId { get; set; } = null!;
        public int BusReservationId { get; set; }
        public BusReservation? BusReservation { get; set; }
        public string CouponCode { get; set; } = string.Empty;
        public DateTime UsedAtUtc { get; set; }
        public decimal TotalFareInr { get; set; }
        public string CouponType { get; set; } = string.Empty;
        public decimal CouponValue { get; set; }
        public decimal CouponAmountInr { get; set; }
        public string BookingStatus { get; set; } = string.Empty;
    }
}
