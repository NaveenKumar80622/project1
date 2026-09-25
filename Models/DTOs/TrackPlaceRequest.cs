namespace PickNBook.Api.Models.DTOs;

public class TrackPlaceRequest
{
    public string CityName { get; set; } = string.Empty;
    public string? CityCode { get; set; }
    public string TripType { get; set; } = string.Empty;
}
