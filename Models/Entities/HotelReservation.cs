using System;

namespace PickNBook.Api.Models
{
    public class HotelReservation
    {
        public int Id { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public string? ProviderBookingId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string HotelId { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string OfferId { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public string GuestName { get; set; } = string.Empty;
        public string GuestEmail { get; set; } = string.Empty;
        public string GuestPhone { get; set; } = string.Empty;
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }
        public int Rooms { get; set; }
        public decimal Price { get; set; }
        public decimal NetPrice { get; set; }
        public decimal MarkupAmount { get; set; }
        public decimal BasePrice { get; set; }
        public decimal ConvenienceFee { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal B2CFinalFare { get; set; } = 0m;
        public string? CouponCode { get; set; }
        public decimal CouponDiscount { get; set; } = 0m;
        public decimal CancellationCharges { get; set; } = 0m;
        public decimal RefundAmount { get; set; } = 0m;
        public string Currency { get; set; } = "INR";
        public string Status { get; set; } = "Booked"; // Booked, Confirmed, Cancelled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancellationReason { get; set; }

        // Guest details for SRDV API
        public string GuestNationality { get; set; } = "IN";
        public string? RoomTypeName { get; set; }

        // SRDV Tracking Fields
        public string? TraceId { get; set; }
        public string? SrdvBookingId { get; set; }
        public string? SrdvBookingResponseJson { get; set; }
        public string? SrdvType { get; set; }
        public string? SrdvIndex { get; set; }

        // SRDV Supplier Extra Booking & Policy Fields
        public string? ConfirmationNo { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? RatePlanCode { get; set; }
        public string? RoomTypeCode { get; set; }
        public DateTime? LastCancellationDate { get; set; }
        public string? CancellationPolicyJson { get; set; }

        // SRDV Supplier Pricing & GST Breakdown
        public decimal SrdvOfferedPrice { get; set; } = 0m;
        public decimal SrdvGstAmount { get; set; } = 0m;
        public decimal SrdvCgstAmount { get; set; } = 0m;
        public decimal SrdvSgstAmount { get; set; } = 0m;
        public decimal SrdvIgstAmount { get; set; } = 0m;
    }
}
