using System;

namespace PickNBook.Api.Models;

public class HotelSearchLog
{
    public int Id { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Adults { get; set; }
    public int Rooms { get; set; }
    public string? UserId { get; set; }
    public DateTime SearchedAtUtc { get; set; } = DateTime.UtcNow;
}
