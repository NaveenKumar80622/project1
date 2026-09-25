public class FeaturedFlightsDto
{
    public FlightOfferDto? CheapestRoute { get; set; }
    public List<FlightOfferDto> UnderBudgetFlights { get; set; } = [];
    public FlightOfferDto? FastestCheapestCombo { get; set; }
    public string? CheapestAirline { get; set; }
    public List<FlightOfferDto> LimitedSeatFlights { get; set; } = [];
    public FlightOfferDto? WeeklyCheapestFlight { get; set; }
}
