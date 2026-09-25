using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models;

public class PlaceSearchStat
{
    public int Id { get; set; }
    public string CityName { get; set; } = string.Empty;
    
    [NotMapped]
    public string? CityCode { get; set; }
    
    public string TripType { get; set; } = string.Empty;
    public long SelectionCount { get; set; }
    
    [NotMapped]
    public DateTime? LastSelectedAtUtc { get; set; }
}
