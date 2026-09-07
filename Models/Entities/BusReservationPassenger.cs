using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models
{
    public class BusReservationPassenger
    {
        public int Id { get; set; }
        public int BusReservationId { get; set; }
        public BusReservation? BusReservation { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string? SeatNumber { get; set; }
        public decimal BaseFareInr { get; set; }
        public string SeatType { get; set; } = string.Empty;
        public int Age { get; set; }
        public bool IsCancelled { get; set; } = false;
        public DateTime? CancelledAtUtc { get; set; }

       
        public string? Title { get; set; }

        
        public string? FirstName { get; set; }

       
        public string? LastName { get; set; }

        
        public bool LeadPassenger { get; set; } = false;

       
        public int? SeatIndex { get; set; }

        
        public decimal? PublishedFareInr { get; set; }

       
        public decimal? GstAmountInr { get; set; }

        
        public decimal? TaxInr { get; set; }

       
        public decimal? OfferedFareInr { get; set; }

        public decimal? GstRate { get; set; }

        public bool? IsUpper { get; set; }
    }
}
