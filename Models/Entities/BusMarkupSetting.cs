namespace PickNBook.Api.Models
{
    public class BusMarkupSetting
    {
        public int Id { get; set; }

        public string SeatType { get; set; } = string.Empty;

        public decimal Value { get; set; }

        public string MarkupType { get; set; } = "Percentage";

        public string Status { get; set; } = "Active";

        public DateTime EntryDateUtc { get; set; }

        public DateTime UpdateDateUtc { get; set; }

        public string? UpdatedBy { get; set; }

        public string? Remark { get; set; }
    }
}
