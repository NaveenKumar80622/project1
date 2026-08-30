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
    }
}
