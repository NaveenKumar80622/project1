using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

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
        private readonly IFlightPricingService _flightPricingService;
        private readonly IHotelMarkupService _hotelMarkupService;
        private readonly AppDbContext _dbContext;

        public CashfreePaymentController(
            IOptions<CashfreeSettings> settings,
            ICashfreeService cashfreeService,
            IPaymentService paymentService,
            ICurrentUserService currentUserService,
            ILogger<CashfreePaymentController> logger,
            IBusPromotionEngineService busPricingService,
            IFlightPricingService flightPricingService,
            IHotelMarkupService hotelMarkupService,
            AppDbContext dbContext)
        {
            _settings = settings.Value;
            _cashfreeService = cashfreeService;
            _paymentService = paymentService;
            _currentUserService = currentUserService;
            _logger = logger;
            _busPricingService = busPricingService;
            _flightPricingService = flightPricingService;
            _hotelMarkupService = hotelMarkupService;
            _dbContext = dbContext;
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

                if (!string.IsNullOrEmpty(request.BookingPayloadJson) && !string.IsNullOrEmpty(request.BookingType))
                {
                    if (request.BookingType == BookingType.Bus)
                    {
                        var payload = JsonSerializer.Deserialize<CreateBusBookingRequestDto>(request.BookingPayloadJson, 
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (payload == null) return BadRequest(new { message = "Invalid Bus Payload" });

                        var traceId = payload.TraceId ?? string.Empty;
                        var blockedSeats = await _dbContext.BusBlockedSeatPrices
                            .Where(x => x.TraceId == traceId)
                            .ToListAsync();

                        if (!blockedSeats.Any())
                        {
                            return BadRequest(new { message = "No active seat block found for this TraceId. Please try booking again." });
                        }

                        var seatPreviews = payload.Passengers?
                            .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                            .Select(p => {
                                var blockedSeat = blockedSeats.FirstOrDefault(b => b.SeatName.Equals(p.SeatNumber, StringComparison.OrdinalIgnoreCase));
                                return new SeatPreviewDto 
                                { 
                                    SeatCode = p.SeatNumber!, 
                                    BaseFare = blockedSeat?.BaseFare > 0 ? blockedSeat.BaseFare : p.BaseFare, 
                                    SeatType = !string.IsNullOrWhiteSpace(p.SeatType) ? p.SeatType : (payload.BusType ?? "Unknown"),
                                    ExternalGst = blockedSeat?.GstAmount > 0 ? blockedSeat.GstAmount : p.ExternalGst 
                                };
                            })
                            .ToList() ?? new List<SeatPreviewDto>();

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

                        string? actualCouponCode = request.CouponCode != null ? request.CouponCode : payload.CouponCode;
                        int? actualPromoId = request.PromotionId != null ? request.PromotionId : payload.PromotionId;

                        var pricing = await _busPricingService.CalculateAsync(
                            dummyBus,
                            seatPreviews,
                            actualCouponCode,
                            actualPromoId,
                            parsedUserId,
                            payload.SelectedFeaturedOfferId);

                        providerAmount = blockedSeats.Sum(x => x.BaseFare) + blockedSeats.Sum(x => x.GstAmount);
                        markupAmount = pricing.Seats.Sum(s => s.MarkupAmount);
                        discountAmount = pricing.TotalDiscount;
                        convenienceFee = 0m;
                        calculatedFinalAmount = pricing.GrandTotal;
                    }
                    else if (request.BookingType == BookingType.Hotel)
                    {
                        var payload = JsonSerializer.Deserialize<HotelBookRequestDto>(request.BookingPayloadJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (payload == null) return BadRequest(new { message = "Invalid Hotel Payload" });

                        var blockedHotel = await _dbContext.HotelBlockedPrices
                            .FirstOrDefaultAsync(h => h.ResultIndex == payload.ResultIndex && h.TraceId == payload.TraceId);

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
                        
                        if (!string.IsNullOrWhiteSpace(request.CouponCode))
                        {
                            var normalizedCoupon = request.CouponCode.Trim().ToUpperInvariant();
                            var coupon = await _dbContext.HotelCoupons.FirstOrDefaultAsync(c => c.CouponCode == normalizedCoupon && c.Status == "Active");
                            if (coupon != null && coupon.ExpiryDate >= DateOnly.FromDateTime(DateTime.UtcNow))
                            {
                                if (coupon.CouponType == "Percentage")
                                    discountAmount = calculatedFinalAmount * (coupon.Value / 100m);
                                else
                                    discountAmount = coupon.Value;

                                if (coupon.MaxDiscountAmount > 0 && discountAmount > coupon.MaxDiscountAmount)
                                    discountAmount = coupon.MaxDiscountAmount;
                            }
                            calculatedFinalAmount -= discountAmount;
                        }
                    }
                    else if (request.BookingType == BookingType.Flight)
                    {
                        using var doc = JsonDocument.Parse(request.BookingPayloadJson);
                        var root = doc.RootElement;
                        
                        string airline = "";
                        string fromCity = "";
                        string toCity = "";
                        string travelClassStr = "Economy";
                        int adults = 1;
                        int children = 0;
                        int infants = 0;
                        DateTime depTime = DateTime.UtcNow.AddDays(1);
                        TripType tripType = TripType.OneWay;

                        if (root.TryGetProperty("Passengers", out var paxArray) && paxArray.ValueKind == JsonValueKind.Array)
                        {
                            adults = paxArray.EnumerateArray().Count(p => p.TryGetProperty("PaxType", out var pt) && pt.GetInt32() == 1);
                            children = paxArray.EnumerateArray().Count(p => p.TryGetProperty("PaxType", out var pt) && pt.GetInt32() == 2);
                            infants = paxArray.EnumerateArray().Count(p => p.TryGetProperty("PaxType", out var pt) && pt.GetInt32() == 3);
                        }

                        if (root.TryGetProperty("Fare", out var fareNode))
                        {
                            providerAmount = fareNode.TryGetProperty("OfferedFare", out var offFare) ? offFare.GetDecimal() : 0m;
                            decimal baseFare = fareNode.TryGetProperty("BaseFare", out var bFare) ? bFare.GetDecimal() : 0m;
                            decimal tax = fareNode.TryGetProperty("Tax", out var tFare) ? tFare.GetDecimal() : 0m;

                            if (providerAmount == 0) providerAmount = baseFare + tax;

                            if (fareNode.TryGetProperty("TotalBaggageCharges", out var bagNode) && decimal.TryParse(bagNode.ToString(), out var parsedBag)) ssrAmount += parsedBag;
                            if (fareNode.TryGetProperty("TotalMealCharges", out var mealNode) && decimal.TryParse(mealNode.ToString(), out var parsedMeal)) ssrAmount += parsedMeal;
                            if (fareNode.TryGetProperty("TotalSeatCharges", out var seatNode) && decimal.TryParse(seatNode.ToString(), out var parsedSeat)) ssrAmount += parsedSeat;
                            if (fareNode.TryGetProperty("TotalSpecialServiceCharges", out var specialNode) && decimal.TryParse(specialNode.ToString(), out var parsedSpecial)) ssrAmount += parsedSpecial;

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

                            markupAmount = pricingBreakdown.MarkupAmount;
                            discountAmount = pricingBreakdown.PromotionDiscount + pricingBreakdown.CouponDiscount;
                            calculatedFinalAmount = pricingBreakdown.FinalAmount + ssrAmount;
                        }
                        else
                        {
                            _logger.LogWarning("Flight payload did not contain Fare node. Refusing to guess the price.");
                            return BadRequest(new { message = "Flight payload is missing strict Fare details." });
                        }
                    }

                    // Strict Price Parity Check
                    if (calculatedFinalAmount != request.OrderAmount)
                    {
                        _logger.LogWarning("Price mismatch detected. Frontend sent {FrontendAmount}, Backend calculated {BackendAmount}", request.OrderAmount, calculatedFinalAmount);
                        return BadRequest(new { message = $"Price mismatch. The calculated final amount is {calculatedFinalAmount}, but the request specified {request.OrderAmount}. Please refresh the pricing." });
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
                        CouponCode = request.CouponCode,
                        OfferCode = request.SelectedFeaturedOfferId?.ToString(),
                        FinalPayableAmount = calculatedFinalAmount,
                        CalculatedAtUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    return BadRequest(new { message = "BookingPayloadJson and BookingType are strictly required for Cashfree orders." });
                }

                // Create Payment Record (using calculatedFinalAmount instead of request.OrderAmount directly)
                var payment = await _paymentService.CreatePaymentAsync(
                    userIdStr, request.BookingType,
                    providerAmount, markupAmount, convenienceFee, discountAmount, 
                    request.CouponCode, request.SelectedFeaturedOfferId?.ToString(),
                    calculatedFinalAmount, request.OrderCurrency);

                // Create Pending Booking
                await _paymentService.CreatePendingBookingAsync(
                    payment.Id, request.BookingType, userIdStr, calculatedFinalAmount, request.OrderCurrency,
                    request.BookingPayloadJson, pricingSnapshotJson, DateTime.UtcNow.AddMinutes(30));

                // Create Cashfree Order
                string notifyUrl = !string.IsNullOrEmpty(_settings.WebhookUrl) ? _settings.WebhookUrl : request.NotifyUrl;
                string orderId = payment.PaymentReference;

                var cfResponse = await _cashfreeService.CreateOrderAsync(
                    orderId, calculatedFinalAmount, request.OrderCurrency,
                    request.CustomerId, request.CustomerName, request.CustomerEmail, request.CustomerPhone,
                    request.ReturnUrl, notifyUrl);

                // Update Payment with Cashfree IDs
                await _paymentService.AssociateCashfreeOrderAsync(payment.Id, cfResponse.OrderId, cfResponse.CfOrderId, cfResponse.PaymentSessionId);

                return Ok(cfResponse);
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
