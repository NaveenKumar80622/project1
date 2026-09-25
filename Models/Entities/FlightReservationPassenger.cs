namespace PickNBook.Api.Models
{
    public class FlightReservationPassenger
    {
        public int Id { get; set; }
        public int FlightReservationId { get; set; }
        public FlightReservation? FlightReservation { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PassengerType { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string? SeatNumber { get; set; }
        
        public string? BaggageJson { get; set; }
        public string? MealJson { get; set; }
        public decimal SsrTotalInr { get; set; }

        public bool IsCancelled { get; set; } = false;
        public DateTime? CancelledAtUtc { get; set; }

        // V8 Passenger Details
        public string? Title { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Nationality { get; set; }
        public string? PassportNo { get; set; }
        public string? TicketNumber { get; set; }
        public string Status { get; set; } = "Booked";
        public string? Email { get; set; }
        public string? ContactNo { get; set; }
        public int? PaxId { get; set; }
        public string? TicketId { get; set; }
    }
}
