using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Services.Implementations;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/cashfree")]
    public class CashfreePaymentController : ControllerBase
    {
        private readonly CashfreeSettings _settings;
        private readonly ICashfreeService _cashfreeService;
        private readonly IPaymentService _paymentService;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CashfreePaymentController> _logger;
        private readonly IBusPromotionEngineService _busPricingService;
        private readonly IBusCouponContextBuilder _busCouponContextBuilder;
        private readonly IFlightPricingService _flightPricingService;
        private readonly IHotelMarkupService _hotelMarkupService;
        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly IWalletReservationService _walletReservationService;
        private readonly IWalletService _walletService;
        private readonly IBackgroundJobQueue _backgroundJobQueue;
        private readonly IServiceScopeFactory _scopeFactory;

        public CashfreePaymentController(
            IOptions<CashfreeSettings> settings,
            ICashfreeService cashfreeService,
            IPaymentService paymentService,
            ICurrentUserService currentUserService,
            ILogger<CashfreePaymentController> logger,
            IBusPromotionEngineService busPricingService,
            IBusCouponContextBuilder busCouponContextBuilder,
            IFlightPricingService flightPricingService,
            IHotelMarkupService hotelMarkupService,
            AppDbContext dbContext,
            IMemoryCache cache,
            IWalletReservationService walletReservationService,
            IWalletService walletService,
            IBackgroundJobQueue backgroundJobQueue,
            IServiceScopeFactory scopeFactory)
        {
            _settings = settings.Value;
            _cashfreeService = cashfreeService;
            _paymentService = paymentService;
            _currentUserService = currentUserService;
            _logger = logger;
            _busPricingService = busPricingService;
            _busCouponContextBuilder = busCouponContextBuilder;
            _flightPricingService = flightPricingService;
            _hotelMarkupService = hotelMarkupService;
            _dbContext = dbContext;
            _cache = cache;
            _walletReservationService = walletReservationService;
            _walletService = walletService;
            _backgroundJobQueue = backgroundJobQueue;
            _scopeFactory = scopeFactory;
        }

        [HttpPost("create-order")]
        [Authorize]
        public async Task<IActionResult> CreateOrder([FromBody] CreateCashfreeOrderRequest request)
        {
            try
            {
                string userIdStr = _currentUserService.GetUserOrGuestId();
                if (string.IsNullOrEmpty(userIdStr))
                {
                    return Unauthorized(new { message = "User not logged in." });
                }

                // Phone Validation for Cashfree (requires 10-15 digits)
                var cleanPhone = new string((request.CustomerPhone ?? "").Where(char.IsDigit).ToArray());
                if (cleanPhone.Length < 10 || cleanPhone.Length > 15)
                {
                    return BadRequest(new { message = "A valid 10-15 digit phone number is required for payment." });
                }
                request.CustomerPhone = cleanPhone;

                decimal providerAmount = 0m;
                decimal markupAmount = 0m;
                decimal discountAmount = 0m;
                decimal convenienceFee = 0m;
                decimal ssrAmount = 0m;
                decimal calculatedFinalAmount = 0m;

                string? pricingSnapshotJson = null;
                string? actualCouponCode = !string.IsNullOrWhiteSpace(request.CouponCode) ? request.CouponCode.Trim() : null;

                string? extractedCustomerName = !string.IsNullOrWhiteSpace(request.CustomerName) ? request.CustomerName.Trim() : null;
                string? extractedCustomerEmail = !string.IsNullOrWhiteSpace(request.CustomerEmail) ? request.CustomerEmail.Trim() : null;
                string? extractedCustomerPhone = !string.IsNullOrWhiteSpace(request.CustomerPhone) ? request.CustomerPhone.Trim() : null;
                int extractedPassengerCount = 1;
                string? extractedPassengerDetailsJson = null;

                if (!string.IsNullOrEmpty(request.BookingPayloadJson) && !string.IsNullOrEmpty(request.BookingType))
                {
                    if (request.BookingType == BookingType.Bus)
                    {
                        var payload = JsonSerializer.Deserialize<CreateBusBookingRequestDto>(request.BookingPayloadJson, 
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (payload == null) return BadRequest(new { message = "Invalid Bus Payload" });

                        var passengerSeats = payload.Passengers?
                            .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                            .Select(p => p.SeatNumber!.Trim())
                            .ToList() ?? new List<string>();

                        if (!passengerSeats.Any())
                        {
                            return BadRequest(new { message = "At least one passenger with a valid seat number is required." });
                        }

                        // Reject duplicate passenger seat numbers
                        var duplicateSeats = passengerSeats
                            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();

                        if (duplicateSeats.Any())
                        {
                            return BadRequest(new { message = $"Duplicate seat number(s) detected: {string.Join(", ", duplicateSeats)}. Each passenger must be assigned a unique seat." });
                        }

                        var traceId = payload.TraceId ?? string.Empty;
                        var blockedSeats = await _dbContext.BusBlockedSeatPrices
                            .Where(x => x.TraceId == traceId)
                            .ToListAsync();

                        if (!blockedSeats.Any())
                        {
                            return BadRequest(new { message = "No active seat block found for this TraceId. Please try booking again." });
                        }

                        // Strict check: every passenger seat must have an authoritative blocked price record with BaseFare > 0
                        var missingBlockedSeats = passengerSeats
                            .Where(seat => !blockedSeats.Any(b => b.SeatName.Equals(seat, StringComparison.OrdinalIgnoreCase) && b.BaseFare > 0))
                            .ToList();

                        if (missingBlockedSeats.Any())
                        {
                            return BadRequest(new { message = $"Authoritative blocked seat pricing is unavailable for seat(s): {string.Join(", ", missingBlockedSeats)}. Please refresh and block the seats again." });
                        }

                        // Authoritative seat layout resolution for SeatType
                        Dictionary<string, BusSeatLayoutItemContext>? layoutMap = null;
                        if (!string.IsNullOrEmpty(payload.TraceId) && !string.IsNullOrEmpty(payload.ResultIndex))
                        {
                            _cache.TryGetValue($"bus_seats_{payload.TraceId}_{payload.ResultIndex}", out layoutMap);
                        }

                        var missingLayoutSeats = passengerSeats
                            .Where(seat => layoutMap == null || 
                                           !layoutMap.TryGetValue(seat, out var layoutSeat) || 
                                           string.IsNullOrWhiteSpace(layoutSeat.SeatType))
                            .ToList();

                        if (missingLayoutSeats.Any())
                        {
                            return BadRequest(new { 
                                message = $"Authoritative seat layout information is unavailable for seat(s): {string.Join(", ", missingLayoutSeats)}. Please refresh the seat layout and block again." 
                            });
                        }

                        var seatPreviews = payload.Passengers!
                            .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                            .Select(p => {
                                var seatCode = p.SeatNumber!.Trim();
                                var blockedSeat = blockedSeats
                                    .OrderByDescending(b => b.Id)
                                    .First(b => b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));

                                var layoutSeat = layoutMap![seatCode];

                                return new SeatPreviewDto 
                                { 
                                    SeatCode = seatCode, 
                                    BaseFare = blockedSeat.BaseFare, 
                                    SeatType = layoutSeat.SeatType, // 100% authoritative from SRDV layout cache
                                    ExternalGst = blockedSeat.GstAmount 
                                };
                            })
                            .ToList();

                        // Ensure pricing parity including dynamically applied checkout coupons/promotions
                        var dummyBus = new PickNBook.Api.Models.BusBooking
                        {
                            FromCity = payload.FromCity,
                            ToCity = payload.ToCity,
                            DepartureTime = string.IsNullOrWhiteSpace(payload.DepartureTime) ? DateTime.UtcNow.AddDays(1) : DateTime.Parse(payload.DepartureTime).ToUniversalTime(),
                            OperatorName = payload.OperatorName ?? "Unknown",
                            BusType = payload.BusType ?? "Unknown",
                            PriceInr = payload.TotalFare,
                            GstCategory = "AC"
                        };

                        int? parsedUserId = null;
                        if (int.TryParse(userIdStr, out var id)) parsedUserId = id;

                        actualCouponCode = !string.IsNullOrWhiteSpace(request.CouponCode)
                            ? request.CouponCode.Trim()
                            : payload.CouponCode?.Trim();

                        if (payload.CouponCode != actualCouponCode)
                        {
                            payload.CouponCode = actualCouponCode;
                            request.BookingPayloadJson = JsonSerializer.Serialize(payload);
                        }

                        int? actualPromoId = request.PromotionId != null ? request.PromotionId : payload.PromotionId;

                        var seatCodes = payload.Passengers?.Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber)).Select(p => p.SeatNumber!).ToList() ?? new();
                        var validationContext = await _busCouponContextBuilder.BuildContextAsync(
                            payload.TraceId,
                            payload.ResultIndex,
                            seatCodes,
                            dummyBus,
                            seatPreviews);

                        var pricing = await _busPricingService.CalculateAsync(
                            dummyBus,
                            seatPreviews,
                            actualCouponCode,
                            actualPromoId,
                            parsedUserId,
                            payload.SelectedFeaturedOfferId,
                            validationContext);

                        // Authoritative customer charge directly consumed from centralized pricing engine
                        calculatedFinalAmount = pricing.FinalAmount;

                        // Provider settlement / reconciliation amount for accounting only (never charged to customer)
                        providerAmount = pricing.Seats.Sum(s => s.BaseFare) + pricing.GstAmount;
                        markupAmount = pricing.Seats.Sum(s => s.MarkupAmount);
                        discountAmount = pricing.TotalDiscount;
                        convenienceFee = pricing.ConvenienceFee;

                        if (payload.Passengers != null && payload.Passengers.Any())
                        {
                            extractedPassengerCount = payload.Passengers.Count;
                            var leadPax = payload.Passengers.FirstOrDefault(p => p.LeadPassenger == true) ?? payload.Passengers.FirstOrDefault();
                            if (leadPax != null)
                            {
                                var leadName = !string.IsNullOrWhiteSpace(leadPax.FullName)
                                    ? leadPax.FullName
                                    : $"{leadPax.Title} {leadPax.FirstName} {leadPax.LastName}".Trim();
                                if (!string.IsNullOrWhiteSpace(leadName))
                                {
                                    extractedCustomerName = leadName;
                                }
                            }
                            extractedPassengerDetailsJson = JsonSerializer.Serialize(payload.Passengers.Select(p => new
                            {
                                Name = !string.IsNullOrWhiteSpace(p.FullName) ? p.FullName : $"{p.Title} {p.FirstName} {p.LastName}".Trim(),
                                p.Gender,
                                p.Age,
                                p.SeatNumber,
                                LeadPassenger = p.LeadPassenger == true
                            }));
                        }
                        if (string.IsNullOrWhiteSpace(extractedCustomerName) && !string.IsNullOrWhiteSpace(payload.PassengerName))
                        {
                            extractedCustomerName = payload.PassengerName.Trim();
                        }
                        if (string.IsNullOrWhiteSpace(extractedCustomerPhone) && !string.IsNullOrWhiteSpace(payload.PassengerPhone))
                        {
                            extractedCustomerPhone = payload.PassengerPhone.Trim();
                        }
                        if (string.IsNullOrWhiteSpace(extractedCustomerEmail) && !string.IsNullOrWhiteSpace(payload.PassengerEmail))
                        {
                            extractedCustomerEmail = payload.PassengerEmail.Trim();
                        }

                        if (string.IsNullOrWhiteSpace(extractedPassengerDetailsJson) && !string.IsNullOrWhiteSpace(extractedCustomerName))
                        {
                            var seatList = payload.Passengers?
                                .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                                .Select(p => p.SeatNumber!.Trim())
                                .ToList() ?? new List<string>();

                            extractedPassengerDetailsJson = JsonSerializer.Serialize(new[]
                            {
                                new
                                {
                                    Name = extractedCustomerName,
                                    Email = extractedCustomerEmail ?? string.Empty,
                                    Phone = extractedCustomerPhone ?? string.Empty,
                                    SeatNumber = string.Join(", ", seatList),
                                    LeadPassenger = true
                                }
                            });
                        }
                    }
                    else if (request.BookingType == BookingType.Hotel)
                    {
                        var payload = JsonSerializer.Deserialize<HotelBookRequestDto>(request.BookingPayloadJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (payload == null) return BadRequest(new { message = "Invalid Hotel Payload" });

                        var allHotelPax = payload.HotelRoomsDetails?.SelectMany(r => r.HotelPassenger ?? new List<HotelPassengerDto>()).ToList() ?? new List<HotelPassengerDto>();
                        if (allHotelPax.Any())
                        {
                            extractedPassengerCount = allHotelPax.Count;
                            var leadPax = allHotelPax.FirstOrDefault(p => p.LeadPassenger) ?? allHotelPax.FirstOrDefault();
                            if (leadPax != null)
                            {
                                var leadName = $"{leadPax.Title} {leadPax.FirstName} {leadPax.LastName}".Trim();
                                if (!string.IsNullOrWhiteSpace(leadName)) extractedCustomerName = leadName;
                                if (!string.IsNullOrWhiteSpace(leadPax.Phoneno)) extractedCustomerPhone = leadPax.Phoneno.Trim();
                                if (!string.IsNullOrWhiteSpace(leadPax.Email)) extractedCustomerEmail = leadPax.Email.Trim();
                            }
                            extractedPassengerDetailsJson = JsonSerializer.Serialize(allHotelPax.Select(p => new
                            {
                                Name = $"{p.Title} {p.FirstName} {p.LastName}".Trim(),
                                p.Email,
                                Phone = p.Phoneno,
                                p.PaxType,
                                p.Age,
                                p.LeadPassenger
                            }));
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(payload.GuestName)) extractedCustomerName = payload.GuestName.Trim();
                            if (!string.IsNullOrWhiteSpace(payload.GuestEmail)) extractedCustomerEmail = payload.GuestEmail.Trim();
                            if (!string.IsNullOrWhiteSpace(payload.GuestPhone)) extractedCustomerPhone = payload.GuestPhone.Trim();
                            if (payload.NoOfRooms > 0) extractedPassengerCount = payload.NoOfRooms;

                            if (!string.IsNullOrWhiteSpace(extractedCustomerName))
                            {
                                extractedPassengerDetailsJson = JsonSerializer.Serialize(new[]
                                {
                                    new
                                    {
                                        Name = extractedCustomerName,
                                        Email = extractedCustomerEmail ?? string.Empty,
                                        Phone = extractedCustomerPhone ?? string.Empty,
                                        PaxType = "Guest",
                                        LeadPassenger = true
                                    }
                                });
                            }
                        }

                        var traceIdStr = payload.TraceId.ToString();
                        var blockedHotel = await _dbContext.HotelBlockedPrices
                            .FirstOrDefaultAsync(h => h.ResultIndex == payload.ResultIndex && h.TraceId == traceIdStr);

                        if (blockedHotel != null)
                        {
                            providerAmount = blockedHotel.OfferedPrice;
                            markupAmount = blockedHotel.MarkupAmount;
                            discountAmount = blockedHotel.DiscountAmount;
                            calculatedFinalAmount = blockedHotel.GrandTotal;
                        }
                        else
                        {
                            return BadRequest(new { message = "Hotel price could not be verified. Please block the room again." });
                        }
                        
                        var couponToApply = !string.IsNullOrWhiteSpace(request.CouponCode)
                            ? request.CouponCode.Trim()
                            : payload.CouponCode?.Trim();

                        if (!string.IsNullOrWhiteSpace(couponToApply))
                        {
                            var normalizedCoupon = couponToApply.ToUpperInvariant();
                            var coupon = await _dbContext.HotelCoupons
                                .Include(c => c.Conditions)
                                .FirstOrDefaultAsync(c => c.CouponCode == normalizedCoupon && c.Status == "Active");

                            if (coupon == null)
                            {
                                return BadRequest(new { message = $"Hotel coupon '{couponToApply}' is invalid or inactive." });
                            }

                            var today = DateOnly.FromDateTime(DateTime.UtcNow);
                            if (today < coupon.StartDate || today > coupon.ExpiryDate)
                            {
                                return BadRequest(new { message = $"Hotel coupon '{normalizedCoupon}' is not valid today." });
                            }

                            if (blockedHotel.GrandTotal < coupon.MinBookingAmount)
                            {
                                return BadRequest(new { message = $"Minimum booking amount of INR {coupon.MinBookingAmount} is required for this coupon." });
                            }

                            if (coupon.UseLimit > 0 && coupon.UsedCount >= coupon.UseLimit)
                            {
                                return BadRequest(new { message = $"Usage limit reached for coupon '{normalizedCoupon}'." });
                            }

                            if (!string.IsNullOrEmpty(userIdStr))
                            {
                                var userObj = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id.ToString() == userIdStr);
                                if (userObj != null && userObj.Role == AuthRoles.Agent)
                                {
                                    return BadRequest(new { message = "Coupons are not valid for B2B Agents." });
                                }

                                var userUsageCount = await _dbContext.HotelCouponUsages
                                    .CountAsync(u => u.CouponCode == normalizedCoupon && u.UserId == userIdStr && u.BookingStatus != "Cancelled");
                                if (userUsageCount >= coupon.MaxUsagePerUser)
                                {
                                    return BadRequest(new { message = $"You have exceeded the maximum usage limit of {coupon.MaxUsagePerUser} times for coupon '{normalizedCoupon}'." });
                                }

                                if (coupon.IsFirstTimeUserOnly)
                                {
                                    var hasPriorBookings = await _dbContext.HotelReservations
                                        .AnyAsync(r => r.UserId == userIdStr && r.Status != "Cancelled" && r.Status != "Failed" && !r.Status.StartsWith("Failed_"));
                                    if (hasPriorBookings)
                                    {
                                        return BadRequest(new { message = $"Coupon '{normalizedCoupon}' is only valid for your first hotel booking." });
                                    }
                                }
                            }

                            DateTime? cinDate = null;
                            if (!string.IsNullOrWhiteSpace(payload.CheckInDate) && DateTime.TryParse(payload.CheckInDate, out var parsedCin))
                            {
                                cinDate = parsedCin;
                            }

                            if (cinDate.HasValue && coupon.Conditions != null && coupon.Conditions.Any())
                            {
                                var dayCond = coupon.Conditions.FirstOrDefault(c => string.Equals(c.ConditionType, "DayOfWeek", StringComparison.OrdinalIgnoreCase));
                                if (dayCond != null && !BusPromotionEngineService.IsDayOfWeekMatching(cinDate.Value.DayOfWeek, dayCond.ConditionOperator, dayCond.Value1))
                                {
                                    return BadRequest(new { message = $"Hotel coupon '{normalizedCoupon}' is not valid for check-in on {cinDate.Value.DayOfWeek}." });
                                }
                            }

                            decimal couponDiscount = 0m;
                            decimal totalBeforeDiscount = blockedHotel.OfferedPrice + blockedHotel.Tax + blockedHotel.MarkupAmount;

                            if (string.Equals(coupon.CouponType, "Percentage", StringComparison.OrdinalIgnoreCase))
                            {
                                couponDiscount = totalBeforeDiscount * (coupon.Value / 100m);
                                if (coupon.MaxDiscountAmount > 0 && couponDiscount > coupon.MaxDiscountAmount)
                                    couponDiscount = coupon.MaxDiscountAmount;
                            }
                            else if (string.Equals(coupon.CouponType, "Flat", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(coupon.CouponType, "Fixed", StringComparison.OrdinalIgnoreCase))
                            {
                                couponDiscount = coupon.Value;
                            }

                            couponDiscount = Math.Min(couponDiscount, totalBeforeDiscount);
                            couponDiscount = decimal.Round(couponDiscount, 2, MidpointRounding.AwayFromZero);

                            discountAmount += couponDiscount;
                            calculatedFinalAmount -= couponDiscount;
                            actualCouponCode = normalizedCoupon;

                            if (payload.CouponCode != actualCouponCode)
                            {
                                payload.CouponCode = actualCouponCode;
                                request.BookingPayloadJson = JsonSerializer.Serialize(payload);
                            }
                        }
                    }
                    else if (request.BookingType == BookingType.Flight)
                    {
                        using var doc = JsonDocument.Parse(request.BookingPayloadJson);
                        var root = doc.RootElement;

                        bool TryGetProp(JsonElement elem, string name, out JsonElement val)
                        {
                            if (elem.TryGetProperty(name, out val)) return true;
                            if (elem.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var p in elem.EnumerateObject())
                                {
                                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        val = p.Value;
                                        return true;
                                    }
                                }
                            }
                            val = default;
                            return false;
                        }
                        
                        string airline = "";
                        string fromCity = "";
                        string toCity = "";
                        string travelClassStr = "Economy";
                        int adults = 1;
                        int children = 0;
                        int infants = 0;
                        DateTime depTime = DateTime.UtcNow.AddDays(1);
                        TripType tripType = TripType.OneWay;

                        if (TryGetProp(root, "Passengers", out var paxArray) && paxArray.ValueKind == JsonValueKind.Array)
                        {
                            var paxList = paxArray.EnumerateArray().ToList();
                            adults = paxList.Count(p => TryGetProp(p, "PaxType", out var pt) && pt.GetInt32() == 1);
                            children = paxList.Count(p => TryGetProp(p, "PaxType", out var pt) && pt.GetInt32() == 2);
                            infants = paxList.Count(p => TryGetProp(p, "PaxType", out var pt) && pt.GetInt32() == 3);

                            if (paxList.Any())
                            {
                                extractedPassengerCount = paxList.Count;
                                var firstPax = paxList.FirstOrDefault();
                                if (firstPax.ValueKind == JsonValueKind.Object)
                                {
                                    string title = TryGetProp(firstPax, "Title", out var tProp) ? tProp.GetString() ?? "" : "";
                                    string fn = TryGetProp(firstPax, "FirstName", out var fnProp) ? fnProp.GetString() ?? "" : "";
                                    string ln = TryGetProp(firstPax, "LastName", out var lnProp) ? lnProp.GetString() ?? "" : "";
                                    var name = $"{title} {fn} {ln}".Trim();
                                    if (!string.IsNullOrWhiteSpace(name))
                                    {
                                        extractedCustomerName = name;
                                    }

                                    if (string.IsNullOrWhiteSpace(extractedCustomerPhone))
                                    {
                                        string phone = TryGetProp(firstPax, "ContactNo", out var pProp) ? pProp.GetString() ?? "" : "";
                                        if (string.IsNullOrWhiteSpace(phone) && TryGetProp(firstPax, "Phone", out var phProp)) phone = phProp.GetString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(phone)) extractedCustomerPhone = phone.Trim();
                                    }
                                    if (string.IsNullOrWhiteSpace(extractedCustomerEmail))
                                    {
                                        string email = TryGetProp(firstPax, "Email", out var eProp) ? eProp.GetString() ?? "" : "";
                                        if (!string.IsNullOrWhiteSpace(email)) extractedCustomerEmail = email.Trim();
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(extractedCustomerPhone))
                                {
                                    string phone = TryGetProp(root, "ContactNo", out var rPhone) ? rPhone.GetString() ?? "" : (TryGetProp(root, "Phone", out var rPh) ? rPh.GetString() ?? "" : "");
                                    if (!string.IsNullOrWhiteSpace(phone)) extractedCustomerPhone = phone.Trim();
                                }
                                if (string.IsNullOrWhiteSpace(extractedCustomerEmail))
                                {
                                    string email = TryGetProp(root, "Email", out var rEmail) ? rEmail.GetString() ?? "" : "";
                                    if (!string.IsNullOrWhiteSpace(email)) extractedCustomerEmail = email.Trim();
                                }

                                extractedPassengerDetailsJson = JsonSerializer.Serialize(paxList.Select(p => new
                                {
                                    Name = $"{(TryGetProp(p, "Title", out var t) ? t.GetString() : "")} {(TryGetProp(p, "FirstName", out var f) ? f.GetString() : "")} {(TryGetProp(p, "LastName", out var l) ? l.GetString() : "")}".Trim(),
                                    Gender = TryGetProp(p, "Gender", out var g) ? g.ToString() : "",
                                    PaxType = TryGetProp(p, "PaxType", out var pt) ? pt.ToString() : "1",
                                    Phone = TryGetProp(p, "ContactNo", out var cp) ? cp.GetString() : (TryGetProp(p, "Phone", out var ph) ? ph.GetString() : null),
                                    Email = TryGetProp(p, "Email", out var em) ? em.GetString() : null,
                                    LeadPassenger = TryGetProp(p, "IsLeadPax", out var lp) ? lp.GetBoolean() : false
                                }));
                            }
                        }

                        if (TryGetProp(root, "AirlineCode", out var rAcode)) airline = rAcode.GetString() ?? "";
                        else if (TryGetProp(root, "Airline", out var rAir)) airline = rAir.GetString() ?? "";

                        if (TryGetProp(root, "FromCity", out var rFrom)) fromCity = rFrom.GetString() ?? "";
                        else if (TryGetProp(root, "Origin", out var rOrig) && rOrig.ValueKind == JsonValueKind.String) fromCity = rOrig.GetString() ?? "";

                        if (TryGetProp(root, "ToCity", out var rTo)) toCity = rTo.GetString() ?? "";
                        else if (TryGetProp(root, "Destination", out var rDest) && rDest.ValueKind == JsonValueKind.String) toCity = rDest.GetString() ?? "";

                        if (TryGetProp(root, "DepartureDate", out var depDateProp) && DateTime.TryParse(depDateProp.GetString(), out var parsedDep))
                        {
                            depTime = parsedDep;
                        }

                        if (TryGetProp(root, "Segments", out var segArray) && segArray.ValueKind == JsonValueKind.Array && segArray.GetArrayLength() > 0)
                        {
                            var firstSeg = segArray[0];
                            if (string.IsNullOrEmpty(airline))
                            {
                                if (TryGetProp(firstSeg, "AirlineCode", out var sAcode)) airline = sAcode.GetString() ?? "";
                                else if (TryGetProp(firstSeg, "Airline", out var sAir) && sAir.ValueKind == JsonValueKind.String) airline = sAir.GetString() ?? "";
                                else if (TryGetProp(firstSeg, "AirlineDetails", out var aDetails) && TryGetProp(aDetails, "AirlineCode", out var adCode)) airline = adCode.GetString() ?? "";
                            }
                            if (string.IsNullOrEmpty(fromCity))
                            {
                                if (TryGetProp(firstSeg, "FromCity", out var sFrom)) fromCity = sFrom.GetString() ?? "";
                                else if (TryGetProp(firstSeg, "Origin", out var sOrig))
                                {
                                    if (sOrig.ValueKind == JsonValueKind.String) fromCity = sOrig.GetString() ?? "";
                                    else if (sOrig.ValueKind == JsonValueKind.Object && TryGetProp(sOrig, "CityCode", out var sCc)) fromCity = sCc.GetString() ?? "";
                                    else if (sOrig.ValueKind == JsonValueKind.Object && TryGetProp(sOrig, "AirportCode", out var sAc)) fromCity = sAc.GetString() ?? "";
                                }
                            }
                            if (string.IsNullOrEmpty(toCity))
                            {
                                var lastSeg = segArray[segArray.GetArrayLength() - 1];
                                if (TryGetProp(lastSeg, "ToCity", out var sTo)) toCity = sTo.GetString() ?? "";
                                else if (TryGetProp(lastSeg, "Destination", out var sDest))
                                {
                                    if (sDest.ValueKind == JsonValueKind.String) toCity = sDest.GetString() ?? "";
                                    else if (sDest.ValueKind == JsonValueKind.Object && TryGetProp(sDest, "CityCode", out var dCc)) toCity = dCc.GetString() ?? "";
                                    else if (sDest.ValueKind == JsonValueKind.Object && TryGetProp(sDest, "AirportCode", out var dAc)) toCity = dAc.GetString() ?? "";
                                }
                            }

                            if (depTime == default || depTime <= DateTime.UtcNow)
                            {
                                if (TryGetProp(firstSeg, "DepartureTime", out var segDep) && DateTime.TryParse(segDep.GetString(), out var parsedSegDep))
                                {
                                    depTime = parsedSegDep;
                                }
                                else if (TryGetProp(firstSeg, "Origin", out var segOrigNode) && TryGetProp(segOrigNode, "DepTime", out var origDep) && DateTime.TryParse(origDep.GetString(), out var parsedOrigDep))
                                {
                                    depTime = parsedOrigDep;
                                }
                            }
                        }

                        if (TryGetProp(root, "Fare", out var fareNode))
                        {
                            providerAmount = TryGetProp(fareNode, "OfferedFare", out var offFare) ? offFare.GetDecimal() : 0m;
                            decimal baseFare = TryGetProp(fareNode, "BaseFare", out var bFare) ? bFare.GetDecimal() : 0m;
                            decimal tax = TryGetProp(fareNode, "Tax", out var tFare) ? tFare.GetDecimal() : 0m;

                            if (providerAmount == 0) providerAmount = baseFare + tax;

                            if (TryGetProp(fareNode, "TotalBaggageCharges", out var bagNode) && decimal.TryParse(bagNode.ToString(), out var parsedBag)) ssrAmount += parsedBag;
                            if (TryGetProp(fareNode, "TotalMealCharges", out var mealNode) && decimal.TryParse(mealNode.ToString(), out var parsedMeal)) ssrAmount += parsedMeal;
                            if (TryGetProp(fareNode, "TotalSeatCharges", out var seatNode) && decimal.TryParse(seatNode.ToString(), out var parsedSeat)) ssrAmount += parsedSeat;
                            if (TryGetProp(fareNode, "TotalSpecialServiceCharges", out var specialNode) && decimal.TryParse(specialNode.ToString(), out var parsedSpecial)) ssrAmount += parsedSpecial;

                            var pricingBreakdown = await _flightPricingService.CalculatePricingAsync(
                                supplierBaseFare: baseFare,
                                supplierTaxAmount: tax,
                                airlineCode: airline,
                                airlineName: airline,
                                origin: fromCity,
                                destination: toCity,
                                departureDate: depTime,
                                travelClass: travelClassStr,
                                tripType: tripType,
                                passengerCount: adults + children + infants,
                                couponCode: request.CouponCode,
                                userId: userIdStr,
                                selectedPromotionId: null
                            );

                            if (!string.IsNullOrWhiteSpace(request.CouponCode) && pricingBreakdown.CouponDiscount == 0)
                            {
                                return BadRequest(new { message = "The applied flight coupon is invalid, expired, or does not meet DayOfWeek conditions." });
                            }

                            markupAmount = pricingBreakdown.MarkupAmount;
                            discountAmount = pricingBreakdown.PromotionDiscount + pricingBreakdown.CouponDiscount;
                            calculatedFinalAmount = pricingBreakdown.FinalAmount + ssrAmount;
                            if (!string.IsNullOrWhiteSpace(pricingBreakdown.CouponCode))
                            {
                                actualCouponCode = pricingBreakdown.CouponCode;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Flight payload did not contain Fare node. Refusing to guess the price.");
                            return BadRequest(new { message = "Flight payload is missing strict Fare details." });
                        }
                    }

                    // Authoritative total fare calculated by server
                    decimal totalFare = calculatedFinalAmount;
                    if (totalFare <= 0)
                    {
                        return BadRequest(new { message = "Calculated total fare must be greater than zero." });
                    }

                    // Strict Price Parity Check (Rounded) when customer is paying full Cashfree
                    if (!request.UseWallet)
                    {
                        if (Math.Round(totalFare, 2) != Math.Round(request.OrderAmount, 2))
                        {
                            _logger.LogWarning("Price mismatch detected. Frontend sent {FrontendAmount}, Backend calculated {BackendAmount}", request.OrderAmount, totalFare);
                            return BadRequest(new { message = $"Price mismatch. The calculated final amount is {Math.Round(totalFare, 2)}, but the request specified {Math.Round(request.OrderAmount, 2)}. Please refresh the pricing." });
                        }
                    }

                    // Populate Snapshot
                    pricingSnapshotJson = JsonSerializer.Serialize(new
                    {
                        BookingType = request.BookingType,
                        ProviderAmount = providerAmount,
                        MarkupAmount = markupAmount,
                        ConvenienceFee = convenienceFee,
                        DiscountAmount = discountAmount,
                        SsrAmount = ssrAmount,
                        CouponCode = actualCouponCode,
                        OfferCode = request.SelectedFeaturedOfferId?.ToString(),
                        FinalPayableAmount = totalFare,
                        CalculatedAtUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    return BadRequest(new { message = "BookingPayloadJson and BookingType are strictly required for Cashfree orders." });
                }

                // Universal safety fallback: ensure PassengerDetailsJson is populated if CustomerName is known
                if (string.IsNullOrWhiteSpace(extractedPassengerDetailsJson) && !string.IsNullOrWhiteSpace(extractedCustomerName))
                {
                    extractedPassengerDetailsJson = JsonSerializer.Serialize(new[]
                    {
                        new
                        {
                            Name = extractedCustomerName,
                            Email = extractedCustomerEmail ?? string.Empty,
                            Phone = extractedCustomerPhone ?? string.Empty,
                            PaxType = "Adult",
                            LeadPassenger = true
                        }
                    });
                }

                // ==========================================
                // PHASE 2C: MULTI-TENDER AMOUNT DETERMINATION
                // ==========================================
                decimal calculatedTotalFare = calculatedFinalAmount;
                decimal walletUsedAmount = 0m;
                decimal gatewayPaidAmount = calculatedTotalFare;
                string paymentMethod = "Cashfree";

                if (request.UseWallet)
                {
                    if (!int.TryParse(userIdStr, out var customerId) || customerId <= 0)
                    {
                        return BadRequest(new { message = "A logged-in user account is required to use wallet balance." });
                    }

                    var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == customerId);
                    if (user == null)
                    {
                        return BadRequest(new { message = "User not found." });
                    }

                    if (!string.Equals(user.WalletStatus, "Active", StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { message = "Customer wallet is not active." });
                    }

                    decimal availableBalance = Math.Max(0m, user.WalletBalance);

                    if (availableBalance <= 0)
                    {
                        // Wallet = 0 -> Cashfree full amount
                        _logger.LogInformation("User {UserId} has zero wallet balance. Proceeding with full Cashfree payment.", customerId);
                        walletUsedAmount = 0m;
                        gatewayPaidAmount = calculatedTotalFare;
                        paymentMethod = "Cashfree";
                    }
                    else
                    {
                        // Calculate authoritative amounts
                        walletUsedAmount = Math.Min(availableBalance, calculatedTotalFare);
                        gatewayPaidAmount = calculatedTotalFare - walletUsedAmount;

                        // Invariant guards
                        if (gatewayPaidAmount < 0) gatewayPaidAmount = 0m;
                        if (walletUsedAmount > calculatedTotalFare) walletUsedAmount = calculatedTotalFare;
                        if (walletUsedAmount + gatewayPaidAmount != calculatedTotalFare)
                        {
                            throw new InvalidOperationException($"Invariant violation: walletUsedAmount ({walletUsedAmount}) + gatewayPaidAmount ({gatewayPaidAmount}) != totalFare ({calculatedTotalFare})");
                        }

                        if (gatewayPaidAmount == 0)
                        {
                            paymentMethod = "Wallet";
                        }
                        else
                        {
                            paymentMethod = "Hybrid";
                        }
                    }
                }

                // ==========================================
                // BRANCH 1: FULL WALLET (Zero Gateway)
                // ==========================================
                if (paymentMethod == "Wallet")
                {
                    string paymentRef = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                    int customerUserId = int.Parse(userIdStr);

                    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? dbTx = null;
                    if (_dbContext.Database.IsRelational())
                    {
                        dbTx = await _dbContext.Database.BeginTransactionAsync();
                    }

                    WalletTransaction walletTx;
                    try
                    {
                        walletTx = await _walletService.DebitAsync(
                            userId: customerUserId,
                            amount: calculatedTotalFare,
                            referenceType: request.BookingType ?? "Booking",
                            refCode: paymentRef,
                            description: $"Full wallet payment for {request.BookingType} ({paymentRef})");
                    }
                    catch (InvalidOperationException ex)
                    {
                        if (dbTx != null) await dbTx.RollbackAsync();
                        _logger.LogWarning(ex, "Full wallet debit failed for User {UserId}, amount {Amount}", customerUserId, calculatedTotalFare);
                        var currentBalance = await _dbContext.Users.Where(u => u.Id == customerUserId).Select(u => u.WalletBalance).FirstOrDefaultAsync();
                        return StatusCode(StatusCodes.Status409Conflict, new
                        {
                            code = "WALLET_BALANCE_CHANGED",
                            message = "Your wallet balance has changed. Please refresh and review your payment summary.",
                            currentWalletBalance = currentBalance
                        });
                    }

                    Payment payment;
                    try
                    {
                        payment = await _paymentService.CreatePaymentAsync(
                            userIdStr, request.BookingType,
                            providerAmount, markupAmount, convenienceFee, discountAmount,
                            actualCouponCode, request.SelectedFeaturedOfferId?.ToString(),
                            finalPayableAmount: 0m,
                            currency: request.OrderCurrency,
                            totalAmount: calculatedTotalFare,
                            walletUsedAmount: calculatedTotalFare,
                            gatewayPaidAmount: 0m,
                            paymentMethod: "Wallet",
                            walletReservationStatus: "None",
                            walletTransactionId: walletTx.Id,
                            gatewayPaymentMethod: null,
                            paymentReference: paymentRef,
                            customerName: extractedCustomerName,
                            customerEmail: extractedCustomerEmail,
                            customerPhone: extractedCustomerPhone,
                            passengerCount: extractedPassengerCount,
                            passengerDetailsJson: extractedPassengerDetailsJson);

                        payment.Status = PaymentStatus.Success;
                        payment.PaidAt = DateTime.UtcNow;
                        payment.FulfillmentStatus = "Pending";
                        await _dbContext.SaveChangesAsync();

                        await _paymentService.CreatePendingBookingAsync(
                            payment.Id, request.BookingType, userIdStr, calculatedTotalFare, request.OrderCurrency,
                            request.BookingPayloadJson, pricingSnapshotJson, DateTime.UtcNow.AddMinutes(30));

                        if (dbTx != null) await dbTx.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Payment creation or persistence failed for full wallet payment {PaymentRef}. Performing technical rollback.", paymentRef);
                        if (dbTx != null)
                        {
                            await dbTx.RollbackAsync();
                        }
                        else
                        {
                            // In-memory fallback technical rollback: restore balance and remove orphan debit
                            var u = await _dbContext.Users.FindAsync(customerUserId);
                            if (u != null)
                            {
                                u.WalletBalance += calculatedTotalFare;
                                var orphanTx = await _dbContext.WalletTransactions.FindAsync(walletTx.Id);
                                if (orphanTx != null) _dbContext.WalletTransactions.Remove(orphanTx);
                                await _dbContext.SaveChangesAsync();
                            }
                        }
                        throw;
                    }
                    finally
                    {
                        if (dbTx != null) await dbTx.DisposeAsync();
                    }

                    // Enqueue immediate fulfillment
                    _backgroundJobQueue.QueueBackgroundWorkItem(async (sp, token) =>
                    {
                        var orchestrator = sp.GetRequiredService<IBookingOrchestratorService>();
                        try
                        {
                            await orchestrator.ProcessFulfillmentAsync(payment.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Background fulfillment failed for full wallet payment {PaymentId}", payment.Id);
                        }
                    });

                    return Ok(new CreateOrderResponseDto
                    {
                        TotalAmount = calculatedTotalFare,
                        WalletUsedAmount = calculatedTotalFare,
                        GatewayPaidAmount = 0m,
                        PaymentMethod = "Wallet",
                        IsWalletFullyPaid = true,
                        PaymentReference = payment.PaymentReference
                    });
                }

                // ==========================================
                // BRANCH 2: HYBRID (Wallet Hold + Cashfree)
                // ==========================================
                if (paymentMethod == "Hybrid")
                {
                    string paymentRef = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                    int customerUserId = int.Parse(userIdStr);

                    WalletTransaction reservationTx;
                    try
                    {
                        reservationTx = await _walletReservationService.ReserveAsync(
                            userId: customerUserId,
                            amount: walletUsedAmount,
                            referenceType: request.BookingType ?? "Booking",
                            refCode: paymentRef,
                            description: $"Wallet hold for {request.BookingType} ({paymentRef})");
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "Wallet reservation failed for User {UserId}, amount {Amount}", customerUserId, walletUsedAmount);
                        var currentBalance = await _dbContext.Users.Where(u => u.Id == customerUserId).Select(u => u.WalletBalance).FirstOrDefaultAsync();
                        return StatusCode(StatusCodes.Status409Conflict, new
                        {
                            code = "WALLET_BALANCE_CHANGED",
                            message = "Your wallet balance has changed. Please refresh and review your payment details.",
                            currentWalletBalance = currentBalance
                        });
                    }

                    Payment payment;
                    try
                    {
                        payment = await _paymentService.CreatePaymentAsync(
                            userIdStr, request.BookingType,
                            providerAmount, markupAmount, convenienceFee, discountAmount,
                            actualCouponCode, request.SelectedFeaturedOfferId?.ToString(),
                            finalPayableAmount: gatewayPaidAmount,
                            currency: request.OrderCurrency,
                            totalAmount: calculatedTotalFare,
                            walletUsedAmount: walletUsedAmount,
                            gatewayPaidAmount: gatewayPaidAmount,
                            paymentMethod: "Hybrid",
                            walletReservationStatus: "Reserved",
                            walletTransactionId: reservationTx.Id,
                            gatewayPaymentMethod: null,
                            paymentReference: paymentRef,
                            customerName: extractedCustomerName,
                            customerEmail: extractedCustomerEmail,
                            customerPhone: extractedCustomerPhone,
                            passengerCount: extractedPassengerCount,
                            passengerDetailsJson: extractedPassengerDetailsJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Payment persistence failed for User {UserId}. Releasing reservation {TxId}", customerUserId, reservationTx.Id);
                        await _walletReservationService.ReleaseReservationAsync(reservationTx.Id, "Payment persistence failed");
                        throw;
                    }

                    try
                    {
                        await _paymentService.CreatePendingBookingAsync(
                            payment.Id, request.BookingType, userIdStr, calculatedTotalFare, request.OrderCurrency,
                            request.BookingPayloadJson, pricingSnapshotJson, DateTime.UtcNow.AddMinutes(30));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Pending booking creation failed. Releasing reservation {TxId}", reservationTx.Id);
                        await _walletReservationService.ReleaseReservationAsync(reservationTx.Id, "Pending booking creation failed");
                        payment.Status = PaymentStatus.Failed;
                        payment.FailureReason = "Pending booking creation failed";
                        payment.WalletReservationStatus = "Released";
                        await _dbContext.SaveChangesAsync();
                        throw;
                    }

                    string notifyUrl = !string.IsNullOrEmpty(_settings.WebhookUrl) ? _settings.WebhookUrl : request.NotifyUrl;
                    string orderId = payment.PaymentReference;
                    CashfreeOrderResponse cfResponse;

                    try
                    {
                        cfResponse = await _cashfreeService.CreateOrderAsync(
                            orderId, gatewayPaidAmount, request.OrderCurrency,
                            request.CustomerId, request.CustomerName, request.CustomerEmail, request.CustomerPhone,
                            request.ReturnUrl, notifyUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Cashfree CreateOrderAsync failed for payment {PaymentId}. Releasing reservation {TxId}", payment.Id, reservationTx.Id);
                        await _walletReservationService.ReleaseReservationAsync(reservationTx.Id, "Cashfree order creation failed: " + ex.Message);
                        payment.Status = PaymentStatus.Failed;
                        payment.FailureReason = "Cashfree order creation failed: " + ex.Message;
                        payment.WalletReservationStatus = "Released";
                        await _dbContext.SaveChangesAsync();
                        throw;
                    }

                    await _paymentService.AssociateCashfreeOrderAsync(payment.Id, cfResponse.OrderId, cfResponse.CfOrderId, cfResponse.PaymentSessionId);

                    return Ok(new CreateOrderResponseDto
                    {
                        TotalAmount = calculatedTotalFare,
                        WalletUsedAmount = walletUsedAmount,
                        GatewayPaidAmount = gatewayPaidAmount,
                        PaymentMethod = "Hybrid",
                        IsWalletFullyPaid = false,
                        CashfreeOrderId = cfResponse.OrderId,
                        PaymentSessionId = cfResponse.PaymentSessionId,
                        CfOrderId = cfResponse.CfOrderId,
                        OrderStatus = cfResponse.OrderStatus
                    });
                }

                // ==========================================
                // BRANCH 3: CASHFREE ONLY
                // ==========================================
                string paymentRefCashfree = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

                var paymentCashfree = await _paymentService.CreatePaymentAsync(
                    userIdStr, request.BookingType,
                    providerAmount, markupAmount, convenienceFee, discountAmount,
                    actualCouponCode, request.SelectedFeaturedOfferId?.ToString(),
                    finalPayableAmount: calculatedTotalFare,
                    currency: request.OrderCurrency,
                    totalAmount: calculatedTotalFare,
                    walletUsedAmount: 0m,
                    gatewayPaidAmount: calculatedTotalFare,
                    paymentMethod: "Cashfree",
                    walletReservationStatus: "None",
                    walletTransactionId: null,
                    gatewayPaymentMethod: null,
                    paymentReference: paymentRefCashfree,
                    customerName: extractedCustomerName,
                    customerEmail: extractedCustomerEmail,
                    customerPhone: extractedCustomerPhone,
                    passengerCount: extractedPassengerCount,
                    passengerDetailsJson: extractedPassengerDetailsJson);

                await _paymentService.CreatePendingBookingAsync(
                    paymentCashfree.Id, request.BookingType, userIdStr, calculatedTotalFare, request.OrderCurrency,
                    request.BookingPayloadJson, pricingSnapshotJson, DateTime.UtcNow.AddMinutes(30));

                string notifyUrlCashfree = !string.IsNullOrEmpty(_settings.WebhookUrl) ? _settings.WebhookUrl : request.NotifyUrl;
                string orderIdCashfree = paymentCashfree.PaymentReference;

                var cfResponseCashfree = await _cashfreeService.CreateOrderAsync(
                    orderIdCashfree, calculatedTotalFare, request.OrderCurrency,
                    request.CustomerId, request.CustomerName, request.CustomerEmail, request.CustomerPhone,
                    request.ReturnUrl, notifyUrlCashfree);

                await _paymentService.AssociateCashfreeOrderAsync(paymentCashfree.Id, cfResponseCashfree.OrderId, cfResponseCashfree.CfOrderId, cfResponseCashfree.PaymentSessionId);

                return Ok(new CreateOrderResponseDto
                {
                    TotalAmount = calculatedTotalFare,
                    WalletUsedAmount = 0m,
                    GatewayPaidAmount = calculatedTotalFare,
                    PaymentMethod = "Cashfree",
                    IsWalletFullyPaid = false,
                    CashfreeOrderId = cfResponseCashfree.OrderId,
                    PaymentSessionId = cfResponseCashfree.PaymentSessionId,
                    CfOrderId = cfResponseCashfree.CfOrderId,
                    OrderStatus = cfResponseCashfree.OrderStatus
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Cashfree order");
                return StatusCode(500, new { message = "Failed to create order: " + ex.Message });
            }
        }

        [HttpGet("orders/{orderId}/payments")]
        [Authorize]
        public async Task<IActionResult> VerifyPayment(string orderId)
        {
            try
            {
                var result = await _paymentService.VerifyPaymentAsync(orderId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify payment {OrderId}", orderId);
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
