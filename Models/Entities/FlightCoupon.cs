namespace PickNBook.Api.Models
{
    public class FlightCoupon
    {
        public int Id { get; set; }
        public decimal Value { get; set; }
        public string CouponType { get; set; } = string.Empty;
        public string CouponCode { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public int UseLimit { get; set; }
        public int UsedCount { get; set; }
        public string Status { get; set; } = "Active";
        public bool IsFirstTimeUserOnly { get; set; } = false;
        public DateTime EntryDateUtc { get; set; }
        public string? Remark { get; set; }
    }
}
