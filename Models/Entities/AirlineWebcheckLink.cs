namespace PickNBook.Api.Models
{
    public class AirlineWebcheckLink
    {
        public int Id { get; set; }
        public string Airline { get; set; } = string.Empty;
        public string AirlineCode { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
