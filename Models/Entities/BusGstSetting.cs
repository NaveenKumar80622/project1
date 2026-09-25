namespace PickNBook.Api.Models
{
    public class BusGstSetting
    {
        public int Id { get; set; }

        public string GstCategory { get; set; } = string.Empty;

        public decimal GstPercent { get; set; }

        public string Status { get; set; } = "Active";

        public DateTime EntryDateUtc { get; set; }

        public DateTime UpdateDateUtc { get; set; }

        public string? UpdatedBy { get; set; }

        public string? Remark { get; set; }
    }
}
