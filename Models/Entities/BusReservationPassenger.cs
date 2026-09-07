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

        [NotMapped]
        public string? Title { get; set; }

        [NotMapped]
        public string? FirstName { get; set; }

        [NotMapped]
        public string? LastName { get; set; }

        [NotMapped]
        public bool LeadPassenger { get; set; } = false;

        [NotMapped]
        public int? SeatIndex { get; set; }

        [NotMapped]
        public decimal? PublishedFareInr { get; set; }

        [NotMapped]
        public decimal? GstAmountInr { get; set; }
    }
}
