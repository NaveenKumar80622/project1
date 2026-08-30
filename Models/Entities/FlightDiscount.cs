using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models
{
    public class FlightDiscount
    {
        public int Id { get; set; }
        public decimal Value { get; set; }
        public string DiscountType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime EntryDateUtc { get; set; }
        public DateTime UpdateDateUtc { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string? Remark { get; set; }
        public string Status { get; set; } = "Active";

        public ICollection<FlightDiscountCondition> Conditions { get; set; } = new List<FlightDiscountCondition>();
    }
}
