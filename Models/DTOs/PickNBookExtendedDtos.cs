using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class PickNBookBalanceLogResponseDto
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public List<BalanceLogEntryDto> Logs { get; set; } = new();
    }

    public class BalanceLogEntryDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal RunningBalance { get; set; }
    }

    public class HotelInfoRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = string.Empty;

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("HotelCode")]
        public string HotelCode { get; set; } = string.Empty;
    }

    public class HotelRoomRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = string.Empty;

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("HotelCode")]
        public string HotelCode { get; set; } = string.Empty;
    }

    public class BlockRoomRequestDto
    {
        public string EndUserIp { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string SrdvType { get; set; } = string.Empty;
        public string SrdvIndex { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public string HotelCode { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string GuestNationality { get; set; } = string.Empty;
        public int NoOfRooms { get; set; } = 1;
        public int ClientReferenceNo { get; set; } = 0;
        public bool IsVoucherBooking { get; set; } = false;

        public List<BlockRoomDetailItemDto> HotelRoomsDetails { get; set; } = new();

        // Optional flat properties for backward/convenient frontend mapping
        public string RoomIndex { get; set; } = string.Empty;
        public string RoomTypeCode { get; set; } = string.Empty;
        public string RoomTypeName { get; set; } = string.Empty;
        public string RatePlanCode { get; set; } = string.Empty;
        public string BedTypeCode { get; set; } = string.Empty;
        public int SmokingPreference { get; set; } = 0;
        public decimal Price { get; set; } = 0m;
    }

    public class FareBreakdownDto
    {
        [JsonPropertyName("baseFare")]
        public decimal BaseFare { get; set; }
        
        [JsonPropertyName("markup")]
        public decimal Markup { get; set; }
        
        [JsonPropertyName("gst")]
        public decimal Gst { get; set; }
        
        [JsonPropertyName("taxes")]
        public decimal Taxes { get; set; }
        
        [JsonPropertyName("totalPaid")]
        public decimal TotalPaid { get; set; }
    }

    public class RefundDetailsDto
    {
        [JsonPropertyName("bookingAmount")]
        public decimal BookingAmount { get; set; }
        
        [JsonPropertyName("cancellationCharges")]
        public decimal CancellationCharges { get; set; }
        
        [JsonPropertyName("refundAmount")]
        public decimal RefundAmount { get; set; }
        
        [JsonPropertyName("refundStatus")]
        public string RefundStatus { get; set; } = string.Empty;
    }

    public class PickNBookBookRoomResponseDto
    {
        public BookResultDto BookResult { get; set; } = new();
    }

    public class BookResultDto
    {
        public HotelSearchErrorDto Error { get; set; } = new();
        public bool VoucherStatus { get; set; }
        public int ResponseStatus { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string HotelBookingStatus { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
        public string ConfirmationNo { get; set; } = string.Empty;
        public string BookingRefNo { get; set; } = string.Empty;
        public int BookingId { get; set; }
        public bool IsPriceChanged { get; set; }
        public bool IsCancellationPolicyChanged { get; set; }
        public FareBreakdownDto? FareBreakdown { get; set; }
    }

    public class HotelBookRequestDto
    {
        public string EndUserIp { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string SrdvType { get; set; } = string.Empty;
        public string SrdvIndex { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public string HotelCode { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string? CouponCode { get; set; }
        public string GuestNationality { get; set; } = string.Empty;
        public int NoOfRooms { get; set; } = 1;
        public int ClientReferenceNo { get; set; } = 0;
        public bool IsVoucherBooking { get; set; } = true;
        
        public List<BookRoomDetailItemDto> HotelRoomsDetails { get; set; } = new();

        // Flat properties for backward compatibility
        public string GuestName { get; set; } = string.Empty;
        public string GuestEmail { get; set; } = string.Empty;
        public string GuestPhone { get; set; } = string.Empty;
        public string RoomIndex { get; set; } = "45srlkt1srlkt29092750";
        public string RoomTypeCode { get; set; } = "1";
        public string RoomTypeName { get; set; } = string.Empty;
        public string RatePlanCode { get; set; } = string.Empty;
        public decimal Price { get; set; } = 0m;
        
        public string CheckInDate { get; set; } = string.Empty;
        public string CheckOutDate { get; set; } = string.Empty;
    }

    public class BookRoomDetailItemDto
    {
        public int ChildCount { get; set; }
        public bool RequireAllPaxDetails { get; set; }
        public string RoomId { get; set; } = string.Empty;
        public string RoomStatus { get; set; } = "Active";
        public string RoomIndex { get; set; } = string.Empty;
        public string RoomTypeCode { get; set; } = "1";
        public string RoomTypeName { get; set; } = string.Empty;
        public string RatePlanCode { get; set; } = string.Empty;
        public string RatePlan { get; set; } = string.Empty;
        public string InfoSource { get; set; } = string.Empty;
        public string SequenceNo { get; set; } = string.Empty;
        public List<HotelRoomDayRateDto> DayRates { get; set; } = new();
        public string SupplierPrice { get; set; } = string.Empty;
        public HotelSearchPriceDto Price { get; set; } = new();
        
        public List<HotelPassengerDto> HotelPassenger { get; set; } = new();

        public string RoomPromotion { get; set; } = string.Empty;
        public List<HotelRoomAmenityDto> Amenities { get; set; } = new();
        public string SmokingPreference { get; set; } = string.Empty;
        public string BedTypes { get; set; } = string.Empty;
        public string HotelSupplements { get; set; } = string.Empty;
        public string LastCancellationDate { get; set; } = string.Empty;
        public List<HotelRoomCancellationPolicyDto> CancellationPolicies { get; set; } = new();
        public string? BedTypeCode { get; set; }
        public string? Supplements { get; set; }
        
        public decimal OfferedPrice { get; set; }
        public decimal B2CBasePrice { get; set; }
        public decimal B2CTotalPrice { get; set; }
    }

    public class HotelPassengerDto
    {
        public string Title { get; set; } = "Mr";
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string Phoneno { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PaxType { get; set; } = "1";
        public bool LeadPassenger { get; set; }
        public string? PassportNo { get; set; }
        public string? PassportIssueDate { get; set; }
        public string? PassportExpDate { get; set; }
        public string? PAN { get; set; }
        public string? GSTCompanyAddress { get; set; }
        public string? GSTCompanyContactNumber { get; set; }
        public string? GSTCompanyName { get; set; }
        public string? GSTNumber { get; set; }
        public string? GSTCompanyEmail { get; set; }
    }

    public class PickNBookHotelCancelResponseDto
    {
        [JsonPropertyName("sendChangeResponse")]
        public SendChangeResponseDto SendChangeResponse { get; set; } = new();

        [JsonPropertyName("cancelResult")]
        public SendChangeResponseDto CancelResult => SendChangeResponse;
    }

    public class SendChangeResponseDto
    {
        [JsonPropertyName("error")]
        public HotelSearchErrorDto Error { get; set; } = new();

        [JsonPropertyName("responseStatus")]
        public int ResponseStatus { get; set; } = 0;

        [JsonPropertyName("srdvType")]
        public string SrdvType { get; set; } = "MixAPI";

        [JsonPropertyName("srdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("traceId")]
        public string TraceId { get; set; } = "";

        [JsonPropertyName("changeRequestId")]
        public int ChangeRequestId { get; set; } = 0;

        [JsonPropertyName("changeRequestStatus")]
        public int ChangeRequestStatus { get; set; } = 3;

        [JsonPropertyName("refundedAmount")]
        public decimal RefundedAmount { get; set; } = 0m;

        [JsonPropertyName("cancellationCharge")]
        public decimal CancellationCharge { get; set; } = 0m;

        [JsonPropertyName("b2BMarkUp")]
        public decimal B2BMarkUp { get; set; } = 0m;

        public RefundDetailsDto? RefundDetails { get; set; }
    }

    public class HotelCancelRequestDto
    {
        [JsonPropertyName("bookingId")]
        public int BookingId { get; set; }

        [JsonPropertyName("changeRequestId")]
        public int ChangeRequestId { get; set; } = 0;

        [JsonPropertyName("requestType")]
        public int RequestType { get; set; } = 4;

        [JsonPropertyName("bookingMode")]
        public int BookingMode { get; set; } = 5;

        [JsonPropertyName("remarks")]
        public string Remarks { get; set; } = "Hotel Cancellation Request";

        [JsonPropertyName("srdvType")]
        public string SrdvType { get; set; } = string.Empty;

        [JsonPropertyName("srdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("endUserIp")]
        public string EndUserIp { get; set; } = "127.0.0.1";

        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("userName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("traceId")]
        public string TraceId { get; set; } = string.Empty;
    }

    public class PickNBookBlockRoomResponseDto
    {
        public BlockRoomResultDto BlockRoomResult { get; set; } = new();
    }

    public class BlockRoomResultDto
    {
        public HotelSearchErrorDto Error { get; set; } = new();
        public string AvailabilityType { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public int ResponseStatus { get; set; }
        public bool GSTAllowed { get; set; }
        public bool IsPackageDetailsMandatory { get; set; }
        public bool IsPackageFare { get; set; }
        public bool IsPriceChanged { get; set; }
        public bool IsCancellationPolicyChanged { get; set; }
        public bool IsHotelPolicyChanged { get; set; }
        public string HotelNorms { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string AddressLine2 { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PinCode { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string HotelContactNo { get; set; } = string.Empty;
        public int StarRating { get; set; }
        public string HotelPolicyDetail { get; set; } = string.Empty;
        public string Latitude { get; set; } = string.Empty;
        public string Longitude { get; set; } = string.Empty;
        public bool BookingAllowedForRoamer { get; set; }
        public List<BlockRoomDetailItemDto> HotelRoomsDetails { get; set; } = new();
    }

    public class BlockRoomDetailItemDto
    {
        public int ChildCount { get; set; }
        public bool RequireAllPaxDetails { get; set; }
        public string RoomId { get; set; } = string.Empty;
        public string RoomStatus { get; set; } = "Active";
        public string RoomIndex { get; set; } = string.Empty;
        public string RoomTypeCode { get; set; } = "1";
        public string RoomTypeName { get; set; } = string.Empty;
        public string RatePlanCode { get; set; } = string.Empty;
        public string RatePlan { get; set; } = string.Empty;
        public string InfoSource { get; set; } = string.Empty;
        public string SequenceNo { get; set; } = string.Empty;
        public List<HotelRoomDayRateDto> DayRates { get; set; } = new();
        public string SupplierPrice { get; set; } = string.Empty;
        public HotelSearchPriceDto Price { get; set; } = new();
        public string RoomPromotion { get; set; } = string.Empty;
        public List<HotelRoomAmenityDto> Amenities { get; set; } = new();
        public string SmokingPreference { get; set; } = string.Empty;
        public string BedTypes { get; set; } = string.Empty;
        public string HotelSupplements { get; set; } = string.Empty;
        public string LastCancellationDate { get; set; } = string.Empty;
        public bool IsPassportMandatory { get; set; }
        public bool IsPANMandatory { get; set; }
        public bool FullRefundAllowed { get; set; }
        public List<HotelRoomCancellationPolicyDto> CancellationPolicies { get; set; } = new();
        public string CancellationPolicy { get; set; } = string.Empty;
        public decimal OfferedPrice { get; set; }
        public decimal B2CBasePrice { get; set; }
        public decimal B2CTotalPrice { get; set; }
        public List<string> Inclusion { get; set; } = new();
        public string BedTypeCode { get; set; } = string.Empty;
        public string Supplements { get; set; } = string.Empty;
    }

    public class PickNBookHotelInfoResponseDto
    {
        public HotelInfoResultDto HotelInfoResult { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public string HotelCode { get => HotelInfoResult.HotelDetails.HotelCode; set => HotelInfoResult.HotelDetails.HotelCode = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string HotelName { get => HotelInfoResult.HotelDetails.HotelName; set => HotelInfoResult.HotelDetails.HotelName = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string Address { get => HotelInfoResult.HotelDetails.Address; set => HotelInfoResult.HotelDetails.Address = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore]
        public string PinCode { get => HotelInfoResult.HotelDetails.PinCode; set => HotelInfoResult.HotelDetails.PinCode = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string Phone { get => HotelInfoResult.HotelDetails.HotelContactNo; set => HotelInfoResult.HotelDetails.HotelContactNo = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string Email { get => HotelInfoResult.HotelDetails.Email; set => HotelInfoResult.HotelDetails.Email = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public List<string> Images { get => HotelInfoResult.HotelDetails.Images; set => HotelInfoResult.HotelDetails.Images = value; }
        [System.Text.Json.Serialization.JsonIgnore]
        public List<string> Facilities { get; set; } = new();
    }

    public class HotelInfoResultDto
    {
        public HotelInfoErrorDto Error { get; set; } = new();
        public string SrdvType { get; set; } = "MixAPI";
        public string ResultIndex { get; set; } = string.Empty;
        public string SrdvIndex { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public HotelDetailsExtendedDto HotelDetails { get; set; } = new();
    }

    public class HotelInfoErrorDto
    {
        public int ErrorCode { get; set; } = 0;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class HotelDetailsExtendedDto
    {
        public string HotelCode { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public double StarRating { get; set; }
        public string HotelURL { get; set; } = string.Empty;
        public string OtherDetails { get; set; } = string.Empty;
        public List<HotelInfoDescriptionDto> Description { get; set; } = new();
        public List<HotelInfoPolicyAndInstructionDto> PolicyAndInstruction { get; set; } = new();
        public List<string> Attractions { get; set; } = new();
        public List<HotelInfoFacilityDto> HotelFacilities { get; set; } = new();
        public string HotelPolicy { get; set; } = string.Empty;
        public string SpecialInstructions { get; set; } = string.Empty;
        public string HotelPicture { get; set; } = string.Empty;
        public List<string> Images { get; set; } = new();
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PinCode { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string HotelContactNo { get; set; } = string.Empty;
        public string FaxNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Latitude { get; set; } = string.Empty;
        public string Longitude { get; set; } = string.Empty;
        public string RoomData { get; set; } = string.Empty;
        public string RoomFacilities { get; set; } = string.Empty;
        public string Services { get; set; } = string.Empty;
    }

    public class HotelInfoDescriptionDto
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Detail { get; set; } = new();
    }

    public class HotelInfoPolicyAndInstructionDto
    {
        public string Name { get; set; } = string.Empty;
        public List<HotelInfoPolicyDataDto> Data { get; set; } = new();
    }

    public class HotelInfoPolicyDataDto
    {
        public string SubName { get; set; } = string.Empty;
        public List<string> Detail { get; set; } = new();
    }

    public class HotelInfoFacilityDto
    {
        public string Name { get; set; } = string.Empty;
        public string FontAwesome { get; set; } = string.Empty;
        public string IcoFont { get; set; } = string.Empty;
    }

    public class HotelRoomCombinationsDto
    {
        public string InfoSource { get; set; } = string.Empty;
        public List<HotelRoomCombinationItemDto> RoomCombination { get; set; } = new();
    }

    public class HotelRoomCombinationItemDto
    {
        public List<int> RoomIndex { get; set; } = new();
    }

    public class PickNBookHotelRoomResponseDto
    {
        public HotelRoomResultDto GetHotelRoomResult { get; set; } = new();
    }

    public class HotelRoomResultDto
    {
        public HotelSearchErrorDto Error { get; set; } = new();
        public string SrdvType { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public string SrdvIndex { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public bool IsPolicyPerStay { get; set; }
        public bool IsUnderCancellationAllowed { get; set; }
        public List<HotelRoomCategoryDetailsDto> HotelRoomsDetails { get; set; } = new();

        [JsonPropertyName("HotelRoomDetails")]
        public List<HotelRoomCategoryDetailsDto> HotelRoomDetails => HotelRoomsDetails;

        public HotelRoomCombinationsDto RoomCombinations { get; set; } = new();
    }

    public class HotelRoomCategoryDetailsDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public List<HotelRoomDetailItemDto> Rooms { get; set; } = new();
        public decimal OfferedPrice { get; set; }
    }

    public class HotelRoomDetailItemDto
    {
        public int ChildCount { get; set; }
        public bool RequireAllPaxDetails { get; set; }
        public string RoomId { get; set; } = string.Empty;
        public string RoomStatus { get; set; } = "Active";
        public string RoomIndex { get; set; } = string.Empty;
        public string RoomTypeCode { get; set; } = string.Empty;
        public string RoomTypeName { get; set; } = string.Empty;
        public string RoomTypeCategory { get; set; } = string.Empty;
        public List<string> Description { get; set; } = new();
        public List<HotelRoomImageDto> RoomImages { get; set; } = new();
        public string RatePlanCode { get; set; } = string.Empty;
        public string RatePlan { get; set; } = string.Empty;
        public string InfoSource { get; set; } = string.Empty;
        public string SequenceNo { get; set; } = string.Empty;
        public List<HotelRoomDayRateDto> DayRates { get; set; } = new();
        public string SupplierPrice { get; set; } = string.Empty;
        public HotelSearchPriceDto Price { get; set; } = new();
        public string RoomPromotion { get; set; } = string.Empty;
        public List<HotelRoomAmenityDto> Amenities { get; set; } = new();
        public string SmokingPreference { get; set; } = string.Empty;
        public string BedTypes { get; set; } = string.Empty;
        public string HotelSupplements { get; set; } = string.Empty;
        public string LastCancellationDate { get; set; } = string.Empty;
        public bool IsPassportMandatory { get; set; }
        public bool IsPANMandatory { get; set; }
        public List<HotelRoomServiceStatusDto> ServicesStatus { get; set; } = new();
        public bool FullRefundAllowed { get; set; }
        public List<HotelRoomCancellationPolicyDto> CancellationPolicies { get; set; } = new();
        public decimal OfferedPrice { get; set; }
        public decimal B2CBasePrice { get; set; }
        public decimal B2CTotalPrice { get; set; }
    }

    public class HotelRoomImageDto
    {
        public string Name { get; set; } = "Main";
        public string Image { get; set; } = string.Empty;
    }

    public class HotelRoomDayRateDto
    {
        public string Date { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class HotelRoomAmenityDto
    {
        public string Name { get; set; } = string.Empty;
        public string FontAwesome { get; set; } = string.Empty;
        public string IcoFont { get; set; } = string.Empty;
    }

    public class HotelRoomServiceStatusDto
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class HotelRoomCancellationPolicyDto
    {
        public decimal Charge { get; set; }
        public int ChargeType { get; set; } = 1;
        public string Currency { get; set; } = "INR";
        public string FromDate { get; set; } = string.Empty;
        public string ToDate { get; set; } = string.Empty;
    }

    // ==========================================
    // EXACT SRDV v8 HOTEL SEARCH REQUEST DTOs (21 LEVELS)
    // ==========================================
    public class SrdvHotelSearchRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = "127.0.0.1";

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>Check-in date in yyyy-MM-dd format</summary>
        [JsonPropertyName("CheckInDate")]
        public string CheckInDate { get; set; } = string.Empty;

        /// <summary>Check-out date in yyyy-MM-dd format</summary>
        [JsonPropertyName("CheckOutDate")]
        public string CheckOutDate { get; set; } = string.Empty;

        [JsonPropertyName("NoOfNights")]
        public string NoOfNights { get; set; } = "1";

        [JsonPropertyName("BookingMode")]
        public string BookingMode { get; set; } = "5";

        [JsonPropertyName("CountryCode")]
        public string CountryCode { get; set; } = "IN";

        [JsonPropertyName("CityId")]
        public string CityId { get; set; } = string.Empty;

        [JsonPropertyName("ResultCount")]
        public string ResultCount { get; set; } = "50";

        [JsonPropertyName("PreferredCurrency")]
        public string PreferredCurrency { get; set; } = "INR";

        [JsonPropertyName("GuestNationality")]
        public string GuestNationality { get; set; } = "IN";

        [JsonPropertyName("RequestType")]
        public string RequestType { get; set; } = string.Empty;

        [JsonPropertyName("NoOfRooms")]
        public string NoOfRooms { get; set; } = "1";

        [JsonPropertyName("RoomGuests")]
        public List<RoomGuestDto> RoomGuests { get; set; } = new();

        [JsonPropertyName("PreferredHotel")]
        public string PreferredHotel { get; set; } = string.Empty;

        [JsonPropertyName("MaxRating")]
        public string MaxRating { get; set; } = "5";

        [JsonPropertyName("MinRating")]
        public string MinRating { get; set; } = "1";

        [JsonPropertyName("ReviewScore")]
        public decimal? ReviewScore { get; set; }

        [JsonPropertyName("IsNearBySearchAllowed")]
        public bool IsNearBySearchAllowed { get; set; }
    }

    public class RoomGuestDto
    {
        [JsonPropertyName("NoOfAdults")]
        public string NoOfAdults { get; set; } = "1";

        [JsonPropertyName("NoOfChild")]
        public string NoOfChild { get; set; } = "0";

        [JsonPropertyName("ChildAge")]
        public List<int>? ChildAge { get; set; } = new();
    }

    // ==========================================
    // EXACT SRDV v8 HOTEL SEARCH RESPONSE DTOs
    // ==========================================
    public class PickNBookHotelSearchResponseDto
    {
        public HotelSearchErrorDto Error { get; set; } = new();
        public object TraceId { get; set; } = 0;
        public string SrdvType { get; set; } = "MixAPI";
        public string CityId { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public string CheckInDate { get; set; } = string.Empty;
        public string CheckOutDate { get; set; } = string.Empty;
        public string PreferredCurrency { get; set; } = "INR";
        public List<HotelSearchNoOfRoomsDto> NoOfRooms { get; set; } = new();
        public List<HotelSearchResultItemDto> Results { get; set; } = new();
    }

    public class HotelSearchErrorDto
    {
        public int ErrorCode { get; set; } = 0;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class HotelSearchNoOfRoomsDto
    {
        public string NoOfAdults { get; set; } = "1";
        public string NoOfChild { get; set; } = "0";
        public List<int> ChildAge { get; set; } = new();
    }

    public class HotelSearchResultItemDto
    {
        public string SrdvIndex { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public decimal OfferedFare { get; set; }
        public string HotelCode { get; set; } = string.Empty;
        public string HotelName { get; set; } = string.Empty;
        public string HotelCategory { get; set; } = string.Empty;
        public double StarRating { get; set; }
        public string HotelDescription { get; set; } = string.Empty;
        public string HotelPromotion { get; set; } = string.Empty;
        public string HotelPolicy { get; set; } = string.Empty;
        public string HotelPicture { get; set; } = string.Empty;
        public string HotelAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PinCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string HotelContactNo { get; set; } = string.Empty;
        public string HotelMap { get; set; } = string.Empty;
        public string Latitude { get; set; } = string.Empty;
        public string Longitude { get; set; } = string.Empty;
        public string HotelLocation { get; set; } = string.Empty;
        public object SupplierPrice { get; set; } = "";
        public List<HotelSearchFacilityItemDto> Facilities { get; set; } = new();
        public List<HotelSearchRoomCategoryDto> Rooms { get; set; } = new();
        public HotelSearchPriceDto Price { get; set; } = new();
    }

    public class HotelSearchFacilityItemDto
    {
        public List<string> FacilitiesNames { get; set; } = new();
        public decimal RoomPrice { get; set; }
    }

    public class HotelSearchRoomCategoryDto
    {
        public string Cateogry { get; set; } = string.Empty;
    }

    public class HotelSearchPriceDto
    {
        [JsonPropertyName("currencyCode")]
        public string CurrencyCode { get; set; } = "INR";
        
        [JsonPropertyName("roomPrice")]
        public decimal RoomPrice { get; set; }
        
        [JsonPropertyName("tax")]
        public decimal Tax { get; set; }
        
        [JsonPropertyName("extraGuestCharge")]
        public decimal ExtraGuestCharge { get; set; }
        
        [JsonPropertyName("childCharge")]
        public decimal ChildCharge { get; set; }
        
        [JsonPropertyName("otherCharges")]
        public decimal OtherCharges { get; set; }
        
        [JsonPropertyName("discount")]
        public decimal Discount { get; set; }
        
        [JsonPropertyName("couponDiscount")]
        public decimal CouponDiscount { get; set; }
        
        [JsonPropertyName("publishedPrice")]
        public decimal PublishedPrice { get; set; }
        
        [JsonPropertyName("publishedPriceRoundedOff")]
        public decimal PublishedPriceRoundedOff { get; set; }
        
        [JsonPropertyName("offeredPrice")]
        public decimal OfferedPrice { get; set; }
        
        [JsonPropertyName("offeredPriceRoundedOff")]
        public decimal OfferedPriceRoundedOff { get; set; }
        
        [JsonPropertyName("agentCommission")]
        public decimal AgentCommission { get; set; }
        
        [JsonPropertyName("agentMarkUp")]
        public decimal AgentMarkUp { get; set; }
        
        [JsonPropertyName("serviceTax")]
        public decimal ServiceTax { get; set; }
        
        [JsonPropertyName("tcs")]
        public decimal TCS { get; set; }
        
        [JsonPropertyName("tds")]
        public decimal TDS { get; set; }
        
        [JsonPropertyName("serviceCharge")]
        public decimal ServiceCharge { get; set; }
        
        [JsonPropertyName("totalGSTAmount")]
        public decimal TotalGSTAmount { get; set; }


        [JsonPropertyName("gst")]
        public HotelSearchGstDto? GST { get; set; }

        [JsonPropertyName("b2CBasePrice")]
        public decimal B2CBasePrice { get; set; }

        [JsonPropertyName("b2CTotalPrice")]
        public decimal B2CTotalPrice { get; set; }

        [JsonPropertyName("b2cFinalFare")]
        public decimal B2CFinalFare { get; set; }
    }

    public class HotelSearchGstDto
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

    public class BalanceRequestDto
    {
        [JsonPropertyName("endUserIp")]
        public string EndUserIp { get; set; } = "127.0.0.1";
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;
        [JsonPropertyName("userName")]
        public string UserName { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class BalanceResponseDto
    {
        [JsonPropertyName("Error")]
        public HotelSearchErrorDto Error { get; set; } = new();

        [JsonPropertyName("Balance")]
        public double Balance { get; set; }

        [JsonPropertyName("CreditLimit")]
        public double CreditLimit { get; set; }
    }

    public class BalanceLogRequestDto
    {
        [JsonPropertyName("endUserIp")]
        public string EndUserIp { get; set; } = "127.0.0.1";
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;
        [JsonPropertyName("userName")]
        public string UserName { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class BalanceLogItemDto
    {
        [JsonPropertyName("ID")]
        public int ID { get; set; }
        [JsonPropertyName("Date")]
        public string Date { get; set; } = string.Empty;
        [JsonPropertyName("ClientID")]
        public string ClientID { get; set; } = string.Empty;
        [JsonPropertyName("ClientName")]
        public string ClientName { get; set; } = string.Empty;
        [JsonPropertyName("Detail")]
        public string Detail { get; set; } = string.Empty;
        [JsonPropertyName("Debit")]
        public double Debit { get; set; }
        [JsonPropertyName("Credit")]
        public double Credit { get; set; }
        [JsonPropertyName("Balance")]
        public double Balance { get; set; }
        [JsonPropertyName("Module")]
        public string Module { get; set; } = string.Empty;
        [JsonPropertyName("TraceID")]
        public string TraceID { get; set; } = string.Empty;
        [JsonPropertyName("RefID")]
        public string RefID { get; set; } = string.Empty;
        [JsonPropertyName("UpdatedBy")]
        public string UpdatedBy { get; set; } = string.Empty;
    }

    public class BalanceLogResponseDto
    {
        [JsonPropertyName("Error")]
        public HotelSearchErrorDto Error { get; set; } = new();

        [JsonPropertyName("Result")]
        public System.Collections.Generic.List<BalanceLogItemDto> Result { get; set; } = new();
    }
    public class HotelPricingPreviewRequestDto
    {
        public string HotelCode { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public decimal B2CBasePrice { get; set; }
        public decimal SrdvGstAmount { get; set; }
        public string CouponCode { get; set; } = string.Empty;
    }

    public class HotelPricingPreviewResponseDto
    {
        public decimal BasePrice { get; set; }
        public decimal SrdvGstAmount { get; set; }
        public decimal AgentMarkup { get; set; }
        public decimal TotalBeforeDiscount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalTotal { get; set; }
        
        public bool IsCouponValid { get; set; }
        public string CouponMessage { get; set; } = string.Empty;
    }
}
