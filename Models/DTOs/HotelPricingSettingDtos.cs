using System;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class HotelPricingSettingResponseDto
    {
        public int Id { get; set; }
        public string MarkupType { get; set; } = "Percentage";
        public decimal MarkupValue { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class CreateHotelPricingSettingDto
    {
        [Required]
        [RegularExpression("^(Flat|Percentage)$", ErrorMessage = "MarkupType must be either 'Flat' or 'Percentage'.")]
        public string MarkupType { get; set; } = "Percentage";

        [Range(0, 1000000, ErrorMessage = "MarkupValue must be greater than or equal to 0.")]
        public decimal MarkupValue { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class UpdateHotelPricingSettingDto
    {
        [Required]
        [RegularExpression("^(Flat|Percentage)$", ErrorMessage = "MarkupType must be either 'Flat' or 'Percentage'.")]
        public string MarkupType { get; set; } = "Percentage";

        [Range(0, 1000000, ErrorMessage = "MarkupValue must be greater than or equal to 0.")]
        public decimal MarkupValue { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
