using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models;

public class BusCity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long CityId { get; set; }

    [NotMapped]
    public long Id
    {
        get => CityId;
        set => CityId = value;
    }

    public string CityName { get; set; } = string.Empty;
    public string? DistrictName { get; set; }
    public string StateName { get; set; } = string.Empty;
    public string? StateCode { get; set; }
    public string CountryName { get; set; } = "India";
    public string CountryCode { get; set; } = "IN";
    public string Type { get; set; } = "CITY";
    public long? ParentCityId { get; set; }
    public string? Timezone { get; set; } = "Asia/Kolkata";

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public string CityCode { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
