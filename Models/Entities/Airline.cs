namespace PickNBook.Api.Models
{
    public class Airline
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string Status { get; set; } = "Active";
    }
}
