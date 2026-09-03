using System;

namespace PickNBook.Api.Models;

public class Airport
{
    public int Id { get; set; }
    public string IataCode { get; set; } = string.Empty;
    public string? IcaoCode { get; set; }
    public string AirportName { get; set; } = string.Empty;
    public string? CityCode { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
