namespace PickNBook.Api.Models
{
    public class BusCouponCondition
    {
        public int Id { get; set; }

        public int BusCouponId { get; set; }

        public BusCoupon? Coupon { get; set; }

        public string ConditionType { get; set; } = string.Empty;

        public string ConditionOperator { get; set; } = "Equals";

        public string Value1 { get; set; } = string.Empty;

        public string? Value2 { get; set; }
    }
}
