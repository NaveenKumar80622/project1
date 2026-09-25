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
    public string? FullName { get; set; }
    public string? DistrictName { get; set; }
    public string? Type { get; set; }
    public int? HotelCount { get; set; }

    // Shared: Bus CityCode / Flight CityCode
    public string? CityCode { get; set; }

    // Bus & Multi-part place fields
    public string? BaseCityName { get; set; }
    public string? SubArea { get; set; }
    public string? DisplayName { get; set; }
    public string? LocationType { get; set; }
    public long? ParentCityId { get; set; }
    public string? ParentCityName { get; set; }
    public string? SearchCityId { get; set; }
    public int ChildStopCount { get; set; }
    public int MatchTier { get; set; } = 999;

    // Common
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? StateName { get; set; }
    public string TripType { get; set; } = "";
}

