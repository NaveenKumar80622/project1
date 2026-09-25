using System;

namespace PickNBook.Api.Models.DTOs
{
    public class CreateHotelMarkupRuleDto
    {
        public string RuleName { get; set; } = "Hotel Markup Rule";
        public string CityCode { get; set; } = "*";
        public string HotelCode { get; set; } = "*";
        public string UserType { get; set; } = "All";
        public string MarkupType { get; set; } = "Flat";
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; } = 0;
        public bool IsActive { get; set; } = true;
    }

    public class UpdateHotelMarkupRuleDto
    {
        public string RuleName { get; set; } = "Hotel Markup Rule";
        public string CityCode { get; set; } = "*";
        public string HotelCode { get; set; } = "*";
        public string UserType { get; set; } = "All";
        public string MarkupType { get; set; } = "Flat";
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; } = 0;
        public bool IsActive { get; set; } = true;
    }

    public class HotelMarkupRuleResponseDto
    {
        public int Id { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public string HotelCode { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public string MarkupType { get; set; } = string.Empty;
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
