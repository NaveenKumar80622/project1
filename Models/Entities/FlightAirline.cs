using System;

namespace PickNBook.Api.Models.Entities
{
    public class FlightAirline
    {
        public string AirlineCode { get; set; } = string.Empty;
        public string AirlineName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
