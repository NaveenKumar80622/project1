using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class HotelOfferDto
    {
        public string OfferId { get; set; } = string.Empty;
        public string HotelId { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Address { get; set; } = string.Empty;
        public string CheckInDate { get; set; } = string.Empty;
        public string CheckOutDate { get; set; } = string.Empty;
        public int RoomQuantity { get; set; }
        public int AdultQuantity { get; set; } = 1;
        public int ChildQuantity { get; set; } = 0;
        public string RoomCategory { get; set; } = string.Empty;
        public string BedType { get; set; } = string.Empty;
        public int Beds { get; set; }
        public string RoomDescription { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "INR";
        public DateTime? CancellationDeadline { get; set; }
        public string CancellationPolicy { get; set; } = string.Empty;
        public string CheckInTime { get; set; } = string.Empty;
        public string CheckOutTime { get; set; } = string.Empty;
        public string PaymentType { get; set; } = string.Empty;

        // Extra SRDV v8 Fields
        public string HotelDescription { get; set; } = string.Empty;
        public string HotelPolicy { get; set; } = string.Empty;
        public string? HotelContactNo { get; set; }
        public decimal? SupplierPrice { get; set; }

        // SRDV v8 Price Level (Level 10.25)
        public decimal RoomPrice { get; set; }
        public decimal Tax { get; set; }
        public decimal ExtraGuestCharge { get; set; }
        public decimal ChildCharge { get; set; }
        public decimal OtherCharges { get; set; }
        public decimal Discount { get; set; }
        public decimal PublishedPrice { get; set; }
        public decimal PublishedPriceRoundedOff { get; set; }
        public decimal OfferedPrice { get; set; }
        public decimal OfferedPriceRoundedOff { get; set; }
        public decimal ServiceTax { get; set; }
        public decimal TDS { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal TotalGSTAmount { get; set; }
        public GSTBreakupDto? GST { get; set; }

        // SRDV Tracking Fields
        public string? TraceId { get; set; }
        public string? ResultIndex { get; set; }
        public int? SrdvIndex { get; set; }
        public string? HotelCode { get; set; }
    }

    public class GSTBreakupDto
    {
        public decimal CGSTAmount { get; set; }
        public decimal CGSTRate { get; set; }
        public decimal CessAmount { get; set; }
        public decimal CessRate { get; set; }
        public decimal IGSTAmount { get; set; }
        public decimal IGSTRate { get; set; }
        public decimal SGSTAmount { get; set; }
        public decimal SGSTRate { get; set; }
        public decimal TaxableAmount { get; set; }
    }

    public class HotelSearchResponseDto
    {
        // SRDV Response Root Level (Level 1..9)
        public string TraceId { get; set; } = string.Empty;
        public string SrdvType { get; set; } = "Hotel";
        public string CityId { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public string CheckInDate { get; set; } = string.Empty;
        public string CheckOutDate { get; set; } = string.Empty;
        public string PreferredCurrency { get; set; } = "INR";

        // Level 10 Results[]
        public string HotelId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public string HotelCategory { get; set; } = string.Empty;
        public double Rating { get; set; }
        public string StarRating { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string HotelDescription { get; set; } = string.Empty;
        public string HotelPromotion { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
        public string HotelPolicy { get; set; } = string.Empty;
        public string HotelPicture { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string HotelAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PinCode { get; set; } = string.Empty;
        public string Country { get; set; } = "India";
        public string? ContactNo { get; set; }
        public string? HotelContactNo { get; set; }
        public string HotelMap { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string HotelLocation { get; set; } = string.Empty;
        public decimal? SupplierPrice { get; set; }
        public List<string> Images { get; set; } = new();
        public List<string> Amenities { get; set; } = new();
        public List<HotelOfferDto> Offers { get; set; } = new();
    }

    public class HotelBookingRequestDto
    {
        [Required]
        public string OfferId { get; set; } = string.Empty;

        [Required]
        public string GuestName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string GuestEmail { get; set; } = string.Empty;

        [Required]
        public string GuestPhone { get; set; } = string.Empty;

        public string? CouponCode { get; set; }
        public string? PaymentMethod { get; set; }

        // Optional fallback fields — used when the offer is NOT found in cache
        // (e.g., direct Swagger test or server restart scenario)
        public string? TraceId { get; set; }
        public string? ResultIndex { get; set; }
        public string? HotelCode { get; set; }
        public string? HotelName { get; set; }
        public decimal? Price { get; set; }
        public string? CheckInDate { get; set; }
        public string? CheckOutDate { get; set; }
        public int? Rooms { get; set; }
        public int? Adults { get; set; }
        public string? CityCode { get; set; }
    }

    public class HotelBookingResponseDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string BookingReference { get; set; } = string.Empty;
        public string? ProviderBookingId { get; set; }
        public string? ConfirmationNo { get; set; }
        public string? InvoiceNumber { get; set; }
        public string HotelId { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string OfferId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string GuestName { get; set; } = string.Empty;
        public string GuestEmail { get; set; } = string.Empty;
        public string GuestPhone { get; set; } = string.Empty;
        public string CheckInDate { get; set; } = string.Empty;
        public string CheckOutDate { get; set; } = string.Empty;
        public int Adults { get; set; }
        public int Children { get; set; }
        public int Rooms { get; set; }
        public decimal Price { get; set; }
        public decimal NetPrice { get; set; }
        public decimal MarkupAmount { get; set; }
        public decimal BasePrice { get; set; }
        public decimal ConvenienceFee { get; set; }
        public decimal GstPercent { get; set; }
        public decimal GstAmount { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string Status { get; set; } = "Booked";
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class HotelBookingHistoryDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string BookingReference { get; set; } = string.Empty;
        public string HotelId { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string Dates { get; set; } = string.Empty; // e.g. "12 Jun 2026 - 15 Jun 2026"
        public string CheckInDate { get; set; } = string.Empty;
        public string CheckOutDate { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ProviderBookingId { get; set; }
        public string? TraceId { get; set; }
        public string GuestName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class HotelCancellationDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string BookingReference { get; set; } = string.Empty;
        public string Status { get; set; } = "Cancelled";
        public DateTime CancelledAt { get; set; }
        public string CancellationReason { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
