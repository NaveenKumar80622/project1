namespace PickNBook.Api.Models.DTOs;

public class TicketEmailRequestDto
{
    public string ToEmail { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string BookingReference { get; set; } = string.Empty;
    public string Airline { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public string Pnr { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public string Terminal { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public int StopsCount { get; set; }
    public int DurationMinutes { get; set; }
    public List<FlightTicketSegmentDto> Segments { get; set; } = new();
}
