using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs;

public class SendFlightTicketEmailRequest
{
    [Required]
    [EmailAddress]
    public string ToEmail { get; set; } = string.Empty;

    [Required]
    public string PassengerName { get; set; } = string.Empty;

    [Required]
    public string BookingReference { get; set; } = string.Empty;

    [Required]
    public string Airline { get; set; } = string.Empty;

    [Required]
    public string Origin { get; set; } = string.Empty;

    [Required]
    public string Destination { get; set; } = string.Empty;

    [Required]
    public DateTime DepartureTime { get; set; }

    [Required]
    public DateTime ArrivalTime { get; set; }

    public string? Pnr { get; set; }
    public string? SeatNumber { get; set; }
    public string? Terminal { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Required]
    public string Currency { get; set; } = "INR";

    [Range(0, 10)]
    public int StopsCount { get; set; }

    [Range(0, int.MaxValue)]
    public int DurationMinutes { get; set; }

    public List<FlightPassengerTicketDto> Passengers { get; set; } = new();
    public List<FlightTicketSegmentDto> Segments { get; set; } = new();

    public string? AgentCompanyName { get; set; }
    public string? AgentLogoUrl { get; set; }
    
    public bool NonRefundable { get; set; }
    public string? CancellationCharges { get; set; }
    public string? PartialSegmentCancellation { get; set; }

    public bool IsPartialCancellation { get; set; }
    public List<FlightPassengerTicketDto> CancelledPassengers { get; set; } = new();
    public List<FlightTicketSegmentDto> CancelledSegments { get; set; } = new();
}

public class FlightPassengerTicketDto
{
    public string FullName { get; set; } = string.Empty;
    public string PassengerType { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string? SeatNumber { get; set; }
    public string? TicketNumber { get; set; }
    public string Status { get; set; } = "Booked";
}

public class FlightTicketSegmentDto
{
    public string Airline { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public string? Pnr { get; set; }
    public string Status { get; set; } = "Booked";
}

