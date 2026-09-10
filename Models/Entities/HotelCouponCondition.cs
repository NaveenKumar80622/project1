namespace PickNBook.Api.Models
{
    public class HotelCouponCondition
    {
        public int Id { get; set; }

        public int HotelCouponId { get; set; }

        public HotelCoupon? Coupon { get; set; }

        public string ConditionType { get; set; } = "DayOfWeek";

        public string ConditionOperator { get; set; } = "Equals";

        public string Value1 { get; set; } = string.Empty;

        public string? Value2 { get; set; }
    }
}
