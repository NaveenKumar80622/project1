using System;

namespace PickNBook.Api.Models;

public class HotelCouponUsage
{
    public int Id { get; set; }
    public int HotelReservationId { get; set; }
    public HotelReservation? HotelReservation { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CouponCode { get; set; } = string.Empty;
    public DateTime UsedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal TotalPrice { get; set; }
    public string CouponType { get; set; } = string.Empty;
    public decimal CouponValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public string BookingStatus { get; set; } = string.Empty; // Confirmed, Cancelled
}
