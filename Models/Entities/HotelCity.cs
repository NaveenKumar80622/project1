using System;

namespace PickNBook.Api.Models;

public class HotelCity
{
    public int Id { get; set; }
    public string CityCode { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
    public string RequestType { get; set; } = string.Empty; // "Special" or "International"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
