using System;

namespace PickNBook.Api.Models
{
    public class B2BCommissionRule
    {
        public int Id { get; set; }
        public string MembershipTier { get; set; } = "Bronze"; // Bronze, Silver, Gold, All
        public string ServiceType { get; set; } = "Flight"; // Flight, Bus, Hotel
        public string CommissionType { get; set; } = "Percentage"; // Percentage, Flat
        public decimal CommissionValue { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
