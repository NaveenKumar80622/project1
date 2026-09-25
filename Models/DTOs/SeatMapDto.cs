namespace PickNBook.Api.Models.DTOs;


public class SeatMapResponseDto
{
    public int TripId { get; set; }
    public string TripType { get; set; } = string.Empty;
    public string? TravelClass { get; set; }

    /// <summary>
    /// Layout identifier the frontend uses to pick the correct
    /// React component.  Comes directly from IBusSeatLayout.LayoutType.
    /// Current values: "seater" | "sleeper" | "mixed" | "semi-sleeper"
    /// </summary>
    public string LayoutType { get; set; } = "sleeper";

    public int TotalSeats { get; set; }
    public int BookedSeats { get; set; }
    public int AvailableSeats { get; set; }

    /// <summary>
    /// Flat list of every seat (used for booking validation).
    /// </summary>
    public List<SeatMapItemDto> Seats { get; set; } = [];

    /// <summary>
    /// Optional sections with rendering hints (columns, aisle position,
    /// section label).  The frontend uses these to render deck headers
    /// and aisle gaps without hard-coding any layout knowledge.
    /// Empty list means "render all seats as a single flat grid".
    /// </summary>
    public List<SeatSectionDto> Sections { get; set; } = [];
    public decimal PriceInr { get; set; }

    public List<SeatDefinitionDto> SeatDefinitions { get; set; }
    = [];
    public string? Variant { get; set; }
    public List<BusPointDto> BoardingPoints { get; set; } = [];
    public List<BusPointDto> DroppingPoints { get; set; } = [];
}

public class SeatMapItemDto
{
    public string SeatCode { get; set; } = string.Empty;
    public bool IsBooked { get; set; }
    public string? Gender { get; set; }
    public decimal PriceInr { get; set; }   // final price (base fare + markup)
    public string SeatType { get; set; } = string.Empty;

    public decimal BaseFare { get; set; }

    public decimal MarkupAmount { get; set; }

    public decimal FareBeforeTax { get; set; }
}

/// <summary>
/// Mirrors SeatSection from the layout layer, but only carries
/// the data needed by the frontend (no business logic).
/// </summary>
public class SeatSectionDto
{
    public string Label { get; set; } = string.Empty;
    public List<string> SeatCodes { get; set; } = [];
    public int ColumnsPerRow { get; set; } = 4;
    public int AisleAfterColumn { get; set; } = -1;
}

public sealed class SeatDefinitionDto
{
    public string SeatCode { get; set; } = string.Empty;

    public string SeatType { get; set; } = string.Empty;

    public string Deck { get; set; } = string.Empty;

    public int Row { get; set; }

    public int Column { get; set; }

    public bool IsSleeper { get; set; }

    public bool IsUpper { get; set; }

    public string? Variant { get; set; }
}