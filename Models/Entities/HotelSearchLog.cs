using System;

namespace PickNBook.Api.Models;

public class HotelSearchLog
{
    public int Id { get; set; }
    public string SearchQuery { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? CityName { get; set; }

    public long? CityId { get; set; }

    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Adults { get; set; }
    public int Rooms { get; set; }
    public string? UserId { get; set; }
    public DateTime SearchedAtUtc { get; set; } = DateTime.UtcNow;
}
