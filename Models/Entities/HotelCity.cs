using System;

namespace PickNBook.Api.Models;

public class HotelCity
{
    public int Id { get; set; }
    public long CityId { get; set; }
    public string CityCode { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string? DistrictName { get; set; }
    public string? StateName { get; set; }
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Type { get; set; } = "CITY";
    public int HotelCount { get; set; }
    public string RequestType { get; set; } = "V8"; // Default unified v8
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
