namespace PickNBook.Api.Models
{
    public class BusDiscount
    {
        public int Id { get; set; }
        public decimal Value { get; set; }
        public string DiscountType { get; set; } = string.Empty;
        public DateTime EntryDateUtc { get; set; }
        public DateTime UpdateDateUtc { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string? Remark { get; set; }
        public string Status { get; set; } = "Active";
        public string? Code { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public bool IsAutoApply { get; set; } = true;

        public bool IsExclusive { get; set; } = true;

        public int Priority { get; set; }

        public decimal MinBookingAmount { get; set; }

        public DateTime? StartDateUtc { get; set; }

        public DateTime? EndDateUtc { get; set; }

        public ICollection<BusDiscountCondition> Conditions
        { get; set; } = new List<BusDiscountCondition>();
    }
}
