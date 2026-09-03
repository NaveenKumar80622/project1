using System;

namespace PickNBook.Api.Models;

public class BusCity
{
    public int Id { get; set; }
    public string CityCode { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string? StateName { get; set; }
    public string? CountryName { get; set; } = "India";
    public string? CountryCode { get; set; } = "IN";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
