namespace PickNBook.Api.Models.DTOs;

public class PlaceSuggestionDto
{
    public string CityName { get; set; } = string.Empty;
    public int UsageCount { get; set; }

    // Flight-specific
    public string? AirportCode { get; set; }
    public string? AirportName { get; set; }

    // Hotel-specific
    public string? CityId { get; set; }

    // Shared: Bus CityCode / Flight CityCode
    public string? CityCode { get; set; }

    // Common
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? StateName { get; set; }
    public string TripType { get; set; } = "";
}
