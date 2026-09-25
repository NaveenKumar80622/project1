using System;

namespace PickNBook.Api.Models
{
    public class AgentMarkupSetting
    {
        public int Id { get; set; }
        public int AgentId { get; set; }
        public User? Agent { get; set; }
        public string ServiceType { get; set; } = "Flight"; // Flight, Bus
        public string MarkupType { get; set; } = "Flat"; // Flat, Percentage
        public decimal MarkupValue { get; set; }
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
