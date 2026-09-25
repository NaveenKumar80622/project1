using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models
{
    public class FlightReservationSegment
    {
        [Key]
        public int Id { get; set; }

        public int FlightReservationId { get; set; }
        
        [ForeignKey("FlightReservationId")]
        public FlightReservation? FlightReservation { get; set; }

        public int SegmentIndicator { get; set; }
        public int TripIndicator { get; set; }

        public string Airline { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;

        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;

        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public int Duration { get; set; }

        public string? Baggage { get; set; }
        public string? CabinBaggage { get; set; }

        public string? Pnr { get; set; }
        
        public string Status { get; set; } = "Booked";
    }
}
