namespace PickNBook.Api.Models.DTOs
{
    public class AdminFeaturedOfferRequestDto
    {
        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string DiscountType { get; set; } = string.Empty;

        public decimal DiscountValue { get; set; }

        public decimal? MaxDiscountAmount { get; set; }

        public decimal MinBookingAmount { get; set; }

        public bool IsActive { get; set; }

        public string BookingType { get; set; } = string.Empty;

        public IFormFile? Image { get; set; }
        public int DisplayOrder { get; set; }

        public DateTime? StartDateUtc { get; set; }

        public DateTime? EndDateUtc { get; set; }

        public int? MaxUsage { get; set; }

        public int UsedCount { get; set; }
    }
}
