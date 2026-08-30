using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SrdvRawHotelSearchResponse
    {
        [JsonConverter(typeof(SafeStringConverter))] public string SrdvType { get; set; } = "MixAPI";
        [JsonConverter(typeof(SafeStringConverter))] public string CityId { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string Remarks { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string CheckInDate { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string CheckOutDate { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string PreferredCurrency { get; set; } = "INR";
        
        [JsonConverter(typeof(SafeStringConverter))] public string TraceId { get; set; } = "";
        
        public SrdvRawError Error { get; set; }
        public List<SrdvRawNoOfRooms> NoOfRooms { get; set; } = new();
        public List<SrdvRawHotelResult> Results { get; set; } = new();
    }

    public class SrdvRawError
    {
        [JsonConverter(typeof(SafeIntConverter))] public int ErrorCode { get; set; }
        [JsonConverter(typeof(SafeStringConverter))] public string ErrorMessage { get; set; } = "";
    }

    public class SrdvRawNoOfRooms
    {
        [JsonConverter(typeof(SafeStringConverter))] public string NoOfAdults { get; set; } = "1";
        [JsonConverter(typeof(SafeStringConverter))] public string NoOfChild { get; set; } = "0";
        public List<JsonElement> ChildAge { get; set; } = new();
    }

    public class SrdvRawHotelResult
    {
        [JsonConverter(typeof(SafeStringConverter))] public string SrdvIndex { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string ResultIndex { get; set; } = "";
        
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal OfferedFare { get; set; }
        
        [JsonConverter(typeof(SafeStringConverter))] public string HotelCode { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelName { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelCategory { get; set; } = "";
        
        [JsonConverter(typeof(SafeDoubleConverter))] public double StarRating { get; set; }
        
        [JsonConverter(typeof(SafeStringConverter))] public string HotelDescription { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelPromotion { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelPolicy { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelPicture { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelAddress { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string City { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string State { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string PinCode { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string Country { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelContactNo { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelMap { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string Latitude { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string Longitude { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string HotelLocation { get; set; } = "";
        
        [JsonConverter(typeof(SafeStringConverter))] public string SupplierPrice { get; set; } = "";

        public List<SrdvRawFacility> Facilities { get; set; } = new();
        public List<SrdvRawRoomCategory> Rooms { get; set; } = new();
        public SrdvRawPrice Price { get; set; } = new();
    }

    public class SrdvRawFacility
    {
        public List<JsonElement> FacilitiesNames { get; set; } = new();
        
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal RoomPrice { get; set; }
    }

    public class SrdvRawRoomCategory
    {
        [JsonConverter(typeof(SafeStringConverter))] public string Cateogry { get; set; } = "";
        [JsonConverter(typeof(SafeStringConverter))] public string Category { get; set; } = "";
    }

    public class SrdvRawPrice
    {
        [JsonConverter(typeof(SafeStringConverter))] public string CurrencyCode { get; set; } = "INR";
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal RoomPrice { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal Tax { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal ExtraGuestCharge { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal ChildCharge { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal OtherCharges { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal Discount { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal PublishedPrice { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal PublishedPriceRoundedOff { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal OfferedPrice { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal OfferedPriceRoundedOff { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal ServiceTax { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal TDS { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal ServiceCharge { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal TotalGSTAmount { get; set; }
        
        public SrdvRawGst GST { get; set; }
    }

    public class SrdvRawGst
    {
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal CGSTAmount { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal CGSTRate { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal CessAmount { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal CessRate { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal IGSTAmount { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal IGSTRate { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal SGSTAmount { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal SGSTRate { get; set; }
        [JsonConverter(typeof(SafeDecimalConverter))] public decimal TaxableAmount { get; set; }
    }
}
