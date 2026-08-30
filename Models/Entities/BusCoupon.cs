namespace PickNBook.Api.Models
{
    public class BusCoupon
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
        public DateTime EntryDateUtc { get; set; }
        public string? Remark { get; set; }
        public int MaxUsagePerUser { get; set; } = 1;
        public decimal MinBookingAmount { get; set; } = 0;
        public bool IsExclusive { get; set; } = true;
        public bool IsAutoApply { get; set; } = false;
        public bool IsFirstTimeUserOnly { get; set; } = false;
        public int Priority { get; set; } = 0;
    }
}
