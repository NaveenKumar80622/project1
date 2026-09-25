namespace PickNBook.Api.Models
{
    public class PopularDestination
    {
        public int Id { get; set; }
        public DateTime EntryDateUtc { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SubTitle { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Placement { get; set; } = "Main";
        public string? Url { get; set; }
        public string Status { get; set; } = "Active";
    }
}
