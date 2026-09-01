using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using PickNBook.Api.Services.SeatLayouts;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Filters;

namespace PickNBook.Api.Controllers
{
    [Authorize]
    public class BusBookingsController(
    AppDbContext dbContext,
      IBusPromotionEngineService promotionEngine,
    ITicketEmailService ticketEmailService,
    IWhatsAppService whatsAppService,
    ICurrentUserService currentUserService,
    ISrdvBusService srdvBusService,
    IMemoryCache cache,
    PickNBook.Api.Services.Interfaces.ICancellationRefundCalculator refundCalculator,
    PickNBook.Api.Services.Interfaces.ICashfreeService cashfreeService,
    ILogger<BusBookingsController> logger) : BaseApiController
    {
        //private const string UserIdHeaderName = "X-User-Id";
        private readonly IBusPromotionEngineService _promotionEngine = promotionEngine;
        private readonly ISrdvBusService _srdvBusService = srdvBusService;

        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);
        private static readonly string[] AllowedPassengerGenders = ["Male", "Female"];
        private readonly IWhatsAppService _whatsAppService = whatsAppService;
        private readonly ITicketEmailService _ticketEmailService = ticketEmailService;

        [HttpGet("user/available")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAvailableCoupons()
        {
            var today = DateOnly.FromDateTime(
                DateTime.UtcNow.AddHours(5.5));

            var coupons = await dbContext.BusCoupons
                .AsNoTracking()
                .Where(x =>
                    x.Status == "Active" &&
                       x.StartDate <= today &&
                    x.ExpiryDate >= today &&
                    (x.UseLimit == 0 || x.UsedCount < x.UseLimit))
                .OrderBy(x => x.ExpiryDate)
                .Select(x => new
                {
                    x.Id,
                    x.CouponCode,
                    x.CouponType,
                    x.Value,
                    x.MinBookingAmount,
                    x.MaxUsagePerUser,
                    x.ExpiryDate,
                    Description = x.Remark
                })
                .ToListAsync();

            return Ok(coupons);
        }

        [HttpGet("search-cities")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchBusCities([FromQuery] string query)
        {
            try
            {
                var cities = await _srdvBusService.SearchBusCitiesAsync(query);
                return Ok(cities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("search")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchBusesProxy([FromBody] BusSearchProxyRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.FromCityCode) || 
                string.IsNullOrWhiteSpace(request.ToCityCode) || 
                string.IsNullOrWhiteSpace(request.DepartDate))
            {
                return BadRequest("FromCityCode, ToCityCode, and DepartDate are required.");
            }

            try
            {
                var rawJson = await _srdvBusService.SearchBusesProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);
                
                if (jsonNode?["Result"]?.AsArray() is var resultNode && resultNode != null)
                {
                    var (seaterMarkup, sleeperMarkup) = await GetBothMarkupsAsync();

                    var journeyDateStr = request.DepartDate; // Format: dd/mm/yyyy or yyyy-MM-dd
                    DateOnly.TryParseExact(journeyDateStr, new[] { "dd/MM/yyyy", "yyyy-MM-dd" }, null, System.Globalization.DateTimeStyles.None, out var journeyDate);

                    // ========================================
                    // 1. LEGACY DB SYNC REMOVED
                    // ========================================
                    // We no longer sync every search result to the bus_bookings table.
                    // The frontend will receive the raw SRDV identifiers and handle them.

                    // Now proceed with the rest of the logic
                    foreach (var busNode in resultNode)
                    {
                        if (busNode == null) continue;

                        // ========================================
                        // 2. APPLY PROMOTIONAL MARKUP
                        // ========================================
                        var sleeperStr = busNode["Sleeper"]?.ToString()?.ToLower();
                        var seaterStr = busNode["Seater"]?.ToString()?.ToLower();

                        var isSleeper = sleeperStr == "true";
                        var isSeater = seaterStr == "true";
                        
                        var priceArray = busNode["Price"]?.AsArray();
                        if (priceArray != null && priceArray.Count > 0)
                        {
                            decimal minimumBaseFare = decimal.MaxValue;
                            decimal markupForDisplayFare = 0;

                            // Pre-scan for the max base fare in case of a hybrid bus
                            decimal maxBaseFareInArray = priceArray.Max(p => decimal.TryParse(p?["BaseFare"]?.ToString(), out var b) ? b : 0);

                            foreach (var priceNode in priceArray)
                            {
                                if (priceNode == null) continue;

                                if (decimal.TryParse(priceNode["BaseFare"]?.ToString(), out decimal baseFare))
                                {
                                    BusMarkupSetting? activeMarkup = null;
                                    
                                    if (isSleeper && isSeater)
                                    {
                                        // Hybrid logic: higher fare is Sleeper, lower is Seater
                                        activeMarkup = (baseFare >= maxBaseFareInArray) ? sleeperMarkup : seaterMarkup;
                                    }
                                    else
                                    {
                                        // Standard logic for single-type buses
                                        activeMarkup = isSleeper ? sleeperMarkup : (isSeater ? seaterMarkup : null);
                                    }

                                    var markupAmount = 0m;
                                    if (activeMarkup != null)
                                    {
                                        markupAmount = CalculateMarkupAmount(baseFare, activeMarkup);
                                        priceNode["MarkUp"] = markupAmount.ToString("F2");

                                        if (decimal.TryParse(priceNode["PublishedFare"]?.ToString(), out decimal pubFare))
                                        {
                                            priceNode["PublishedFare"] = (pubFare + markupAmount).ToString("F2");
                                        }
                                    }

                                    // Track markup for the absolute lowest base fare to apply to DisplayFare
                                    if (baseFare < minimumBaseFare)
                                    {
                                        minimumBaseFare = baseFare;
                                        markupForDisplayFare = markupAmount;
                                    }
                                }
                            }

                            if (decimal.TryParse(busNode["DisplayFare"]?.ToString(), out decimal displayFare))
                            {
                                busNode["DisplayFare"] = (displayFare + markupForDisplayFare).ToString("F2");
                            }

                            // B2C Display Fare: Base + Markup only (no GST) for frontend bus cards
                            busNode["B2CDisplayFare"] = (minimumBaseFare + markupForDisplayFare).ToString("F2");
                        }


                    }
                }

                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch buses from SRDV.");
                return StatusCode(500, new { message = "Error fetching buses from provider." });
            }
        }

        [HttpGet("hot-routes")]
        [AllowAnonymous]
        public async Task<IActionResult> GetHotRoutes([FromQuery] string metric = "score")
        {
            var normalizedMetric = metric.Trim().ToLowerInvariant();
            if (normalizedMetric is not ("score" or "search" or "booking"))
            {
                return BadRequest("metric must be one of: score, search, booking.");
            }

            var query = dbContext.BusRouteStats.AsNoTracking();

            query = normalizedMetric switch
            {
                "search" => query.OrderByDescending(x => x.SearchCount).ThenByDescending(x => x.BookingCount),
                "booking" => query.OrderByDescending(x => x.BookingCount).ThenByDescending(x => x.SearchCount),
                _ => query.OrderByDescending(x => x.SearchCount + (x.BookingCount * 3)).ThenByDescending(x => x.BookingCount)
            };

            var response = await query
                .Take(10)
                .Select(x => new
                {
                    x.FromCity,
                    x.ToCity,
                    x.SearchCount,
                    x.BookingCount,
                    Score = x.SearchCount + (x.BookingCount * 3),
                    x.LastSearchedAtUtc,
                    x.LastBookedAtUtc
                })
                .ToListAsync();

            return Ok(response);
        }
        [HttpPost("seat-layout")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSeatLayoutProxy([FromBody] BusSeatLayoutProxyRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.TraceId) || 
                string.IsNullOrWhiteSpace(request.SrdvIndex) || 
                string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest("TraceId, SrdvIndex, and ResultIndex are required.");
            }

            try
            {
                var rawJson = await _srdvBusService.GetSeatLayoutProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);
                
                if (jsonNode is System.Text.Json.Nodes.JsonObject jsonObj)
                {
                    var (seaterMarkup, sleeperMarkup) = await GetBothMarkupsAsync();

                    var resultNodes = new List<System.Text.Json.Nodes.JsonNode>();

                    void ExtractSeats(System.Text.Json.Nodes.JsonNode? deckNode)
                    {
                        if (deckNode == null) return;

                        // Sometimes SRDV returns the deck as an Array of rows
                        if (deckNode is System.Text.Json.Nodes.JsonArray deckArray)
                        {
                            foreach (var rowNode in deckArray)
                            {
                                if (rowNode is System.Text.Json.Nodes.JsonArray rowArray)
                                {
                                    foreach (var seatNode in rowArray)
                                        if (seatNode != null) resultNodes.Add(seatNode);
                                }
                                else if (rowNode is System.Text.Json.Nodes.JsonObject rowObj)
                                {
                                    foreach (var colProperty in rowObj)
                                    {
                                        if (colProperty.Value != null) resultNodes.Add(colProperty.Value);
                                    }
                                }
                            }
                        }
                        // Sometimes SRDV returns the deck as an Object with row keys
                        else if (deckNode is System.Text.Json.Nodes.JsonObject deckObj)
                        {
                            foreach (var rowProperty in deckObj)
                            {
                                var rowVal = rowProperty.Value;
                                if (rowVal != null)
                                {
                                    if (rowVal is System.Text.Json.Nodes.JsonArray rowArray)
                                    {
                                        foreach (var seatNode in rowArray)
                                            if (seatNode != null) resultNodes.Add(seatNode);
                                    }
                                    else if (rowVal is System.Text.Json.Nodes.JsonObject rowObj)
                                    {
                                        foreach (var colProperty in rowObj)
                                        {
                                            var seatNode = colProperty.Value;
                                            if (seatNode != null) resultNodes.Add(seatNode);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    ExtractSeats(jsonObj["Result"]);
                    ExtractSeats(jsonObj["ResultUpperSeat"]);

                    foreach (var seatNode in resultNodes)
                    {
                        if (seatNode == null) continue;

                        var seatType = seatNode["SeatType"]?.ToString()?.ToLower() ?? "";
                        var isSleeper = seatType.Contains("sleeper");
                        
                        var activeMarkup = isSleeper ? sleeperMarkup : seaterMarkup;

                        if (activeMarkup != null)
                        {
                            var priceNode = seatNode["Price"]?.AsObject();
                            if (priceNode != null && decimal.TryParse(priceNode["BaseFare"]?.ToString(), out decimal baseFare))
                            {
                                var markupAmount = CalculateMarkupAmount(baseFare, activeMarkup);

                                priceNode["AgentMarkUp"] = markupAmount.ToString("F2");

                                if (decimal.TryParse(priceNode["PublishedFare"]?.ToString(), out decimal pubFare))
                                {
                                    priceNode["PublishedFare"] = (pubFare + markupAmount).ToString("F2");
                                }
                                
                                if (decimal.TryParse(seatNode["SeatFare"]?.ToString(), out decimal seatFare))
                                {
                                    seatNode["SeatFare"] = (seatFare + markupAmount).ToString("F2");
                                }

                                // B2C Display Fare: Base + Markup only (no GST) for frontend seat icons
                                priceNode["B2CDisplayFare"] = (baseFare + markupAmount).ToString("F2");
                            }
                        }


                    }

                    // Cancellation policies are returned to frontend directly inside the JSON response.
                    // Legacy code to save them to the database has been removed.
                }

                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch seat layout from SRDV proxy.");
                return StatusCode(500, new { message = "Error fetching seat layout from provider." });
            }
        }

        [HttpPost("boarding-points")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBoardingPointsProxy([FromBody] BusBoardingPointsProxyRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.TraceId) || 
                string.IsNullOrWhiteSpace(request.SrdvIndex) || 
                string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest("TraceId, SrdvIndex, and ResultIndex are required.");
            }

            try
            {
                var rawJson = await _srdvBusService.GetBoardingPointDetailsProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);
                
                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch boarding points from SRDV proxy.");
                return StatusCode(500, new { message = "Error fetching boarding points from provider." });
            }
        }

        [HttpPost("block")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> BlockBusProxy([FromBody] SrdvBusBookingRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.TraceId) || 
                string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest("TraceId and ResultIndex are required.");
            }

            if (request.Passengers == null || request.Passengers.Count == 0)
            {
                return BadRequest(new { message = "At least one passenger is required." });
            }

            // ID Proof validation is now handled natively by the frontend reading the Search response flag,
            // and strictly enforced by the SRDV API natively.

            try
            {
                var rawJson = await _srdvBusService.BlockBusProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);
                
                // ==========================================
                // LEGACY POLICY CAPTURE REMOVED
                // ==========================================
                if (jsonNode is System.Text.Json.Nodes.JsonObject jsonObj)
                {
                    var resultObj = jsonObj["Result"] as System.Text.Json.Nodes.JsonObject;

                    // ==========================================
                    // INJECT MARKUP INTO BLOCK RESPONSE
                    // ==========================================
                    var (seaterMarkup, sleeperMarkup) = await GetBothMarkupsAsync();
                    
                    var passengersArray = jsonObj["Passengers"]?.AsArray() ?? resultObj?["Passengers"]?.AsArray();

                    var dbContext = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var blockedSeatsInDb = await dbContext.BusBlockedSeatPrices.Where(x => x.TraceId == request.TraceId).ToListAsync();

                    if (passengersArray != null)
                    {
                        foreach (var passengerNode in passengersArray)
                        {
                            var seatNode = passengerNode?["Seat"]?.AsObject();
                            if (seatNode != null)
                            {
                                var seatNameStr = seatNode["SeatName"]?.ToString();
                                var seatType = seatNode["SeatType"]?.ToString()?.ToLower() ?? "";
                                var isSleeper = seatType.Contains("sleeper");
                                var activeMarkup = isSleeper ? sleeperMarkup : seaterMarkup;

                                decimal finalMarkup = 0m;
                                if (activeMarkup != null)
                                {
                                    var priceNode = seatNode["Price"]?.AsObject();
                                    if (priceNode != null && decimal.TryParse(priceNode["BaseFare"]?.ToString(), out decimal baseFare))
                                    {
                                        finalMarkup = CalculateMarkupAmount(baseFare, activeMarkup);
                                        priceNode["AgentMarkUp"] = finalMarkup.ToString("F2");

                                        if (decimal.TryParse(priceNode["PublishedFare"]?.ToString(), out decimal pubFare))
                                        {
                                            priceNode["PublishedFare"] = (pubFare + finalMarkup).ToString("F2");
                                        }

                                        if (decimal.TryParse(seatNode["SeatFare"]?.ToString(), out decimal seatFare))
                                        {
                                            seatNode["SeatFare"] = (seatFare + finalMarkup).ToString("F2");
                                        }
                                    }
                                }

                                // Update the DB record with the exact breakdown, EVEN IF markup is 0
                                var dbRecord = blockedSeatsInDb.FirstOrDefault(x => x.SeatName == seatNameStr);
                                if (dbRecord != null)
                                {
                                    dbRecord.MarkupAmount = finalMarkup;
                                    dbRecord.DiscountAmount = 0; // Handled later if coupon applied
                                    dbRecord.GrandTotal = dbRecord.BaseFare + dbRecord.GstAmount + finalMarkup;
                                }
                            }
                        }
                        
                        if (blockedSeatsInDb.Any())
                        {
                            await dbContext.SaveChangesAsync();
                        }
                    }
                }



                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to block bus seat from SRDV proxy.");
                return StatusCode(500, new { message = "Error blocking seat from provider." });
            }
        }

        [Obsolete("Use POST /{busId}/pricing-preview instead")]
        [HttpGet("pricing-config")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPricingConfig(
    [FromQuery] string seatType,
    [FromQuery] string gstCategory,
    [FromQuery] decimal baseFare)
        {
            var markup =
                await GetActiveSeatMarkupAsync(seatType);

            var markupAmount =
                CalculateMarkupAmount(baseFare, markup);

            var sellingFare =
                baseFare + markupAmount;

            var gstSetting =
                await GetActiveBusGstAsync(gstCategory);

            var gstPercent =
                gstSetting?.GstPercent ?? 0m;

            var gstAmount = decimal.Round(
                sellingFare * gstPercent / 100m,
                2,
                MidpointRounding.AwayFromZero);

            var convenienceFee =
                await GetActiveBusConvenienceFeeAsync();

            var grandTotal =
                sellingFare +
                gstAmount +
                convenienceFee;

            return Ok(new
            {
                baseFare,

                markupType = markup?.MarkupType,
                markupValue = markup?.Value ?? 0,
                markupAmount,

                sellingFare,

                gstPercent,
                gstAmount,

                convenienceFee,

                grandTotal
            });
        }
        [HttpPost("pricing-preview")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPricingPreview([FromBody] BusPricingPreviewRequestDto request)
        {
            var userIdStr = currentUserService.GetUserOrGuestId();
            int? parsedUserId = null;
            if (int.TryParse(userIdStr, out var id))
            {
                parsedUserId = id;
            }

            try
            {
                // ========================================
                // UNIFIED PROMOTION VALIDATION
                // ========================================
                BusPromotion? promotionToApply = null;
                FeaturedOffer? selectedFeaturedOffer = null;

                if (request.SelectedFeaturedOfferId.HasValue)
                {
                    selectedFeaturedOffer = await dbContext.FeaturedOffers
                        .Include(x => x.Conditions)
                        .FirstOrDefaultAsync(x => x.Id == request.SelectedFeaturedOfferId.Value && x.IsActive);

                    if (selectedFeaturedOffer == null)
                        throw new Exception("Selected offer is invalid or inactive.");

                    if (!string.IsNullOrWhiteSpace(request.CouponCode))
                    {
                        throw new Exception("Featured offers cannot stack with manual coupons.");
                    }
                    if (request.PromotionId.HasValue)
                    {
                        throw new Exception("Only one manual promotion/offer can be applied.");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(request.CouponCode))
                {
                    var normalizedCoupon = request.CouponCode.Trim().ToUpperInvariant();
                    var promoByCode = await dbContext.BusPromotions
                        .Include(x => x.Conditions)
                        .FirstOrDefaultAsync(x => x.Code == normalizedCoupon && x.IsActive && !x.IsAutoApply);

                    if (promoByCode == null)
                    {
                        throw new Exception("Invalid or inactive coupon code.");
                    }

                    if (request.PromotionId.HasValue && request.PromotionId.Value != promoByCode.Id)
                    {
                        throw new Exception("Only one manual promotion/offer can be applied.");
                    }

                    promotionToApply = promoByCode;
                }
                else if (request.PromotionId.HasValue)
                {
                    var promoById = await dbContext.BusPromotions
                        .Include(x => x.Conditions)
                        .FirstOrDefaultAsync(x => x.Id == request.PromotionId.Value && x.IsActive && !x.IsAutoApply);

                    if (promoById == null)
                    {
                        throw new Exception("Invalid or inactive promotion.");
                    }

                    promotionToApply = promoById;
                }

                if (selectedFeaturedOffer != null)
                {
                    var nowUtc = DateTime.UtcNow;

                    if (selectedFeaturedOffer.StartDateUtc.HasValue && selectedFeaturedOffer.StartDateUtc.Value > nowUtc)
                        throw new Exception("Featured offer has not started yet.");

                    if (selectedFeaturedOffer.EndDateUtc.HasValue && selectedFeaturedOffer.EndDateUtc.Value < nowUtc)
                        throw new Exception("Featured offer has expired.");

                    if (selectedFeaturedOffer.MaxUsage.HasValue && selectedFeaturedOffer.UsedCount >= selectedFeaturedOffer.MaxUsage.Value)
                        throw new Exception("Featured offer usage limit reached.");
                }
                else if (promotionToApply != null)
                {
                    var nowUtc = DateTime.UtcNow;

                    if (promotionToApply.StartDateUtc.HasValue && promotionToApply.StartDateUtc.Value > nowUtc)
                        throw new Exception("Promotion has not started yet.");

                    if (promotionToApply.EndDateUtc.HasValue && promotionToApply.EndDateUtc.Value < nowUtc)
                        throw new Exception("Promotion has expired.");

                    if (promotionToApply.MaxUsage.HasValue && promotionToApply.UsedCount >= promotionToApply.MaxUsage.Value)
                        throw new Exception("Promotion usage limit reached.");

                    if (promotionToApply.MaxUsagePerUser > 0 && userIdStr != null)
                    {
                        var userPromoUsageCount = await dbContext.BusPromotionUsages
                            .CountAsync(x => x.BusPromotionId == promotionToApply.Id && x.UserId == userIdStr && x.BookingStatus == "Booked");

                        if (userPromoUsageCount >= promotionToApply.MaxUsagePerUser)
                            throw new Exception("Your usage limit for this promotion has been reached.");
                    }

                    if (promotionToApply.IsFirstTimeUserOnly && userIdStr != null)
                    {
                        var userBookingHistoryService = HttpContext.RequestServices.GetRequiredService<IUserBookingHistoryService>();
                        var hasPrior = await userBookingHistoryService.HasPriorBookingAsync(userIdStr, null);
                        if (hasPrior)
                            throw new Exception("This promotion is only valid for your first booking.");
                    }
                }

                // ========================================
                // CENTRALIZED PRICING ENGINE
                // ========================================
                var traceId = request.TraceId ?? string.Empty;
                var blockedSeats = await dbContext.BusBlockedSeatPrices
                    .Where(x => x.TraceId == traceId)
                    .ToListAsync();

                var seatPreviews = request.Seats
                    .Where(p => !string.IsNullOrWhiteSpace(p.SeatCode))
                    .Select(p => {
                        var blockedSeat = blockedSeats.FirstOrDefault(b => b.SeatName.Equals(p.SeatCode, StringComparison.OrdinalIgnoreCase));
                        return new PickNBook.Api.Models.DTOs.SeatPreviewDto 
                        { 
                            SeatCode = p.SeatCode, 
                            BaseFare = blockedSeat?.BaseFare > 0 ? blockedSeat.BaseFare : p.BaseFare, 
                            SeatType = p.SeatType,
                            ExternalGst = blockedSeat?.GstAmount > 0 ? blockedSeat.GstAmount : p.ExternalGst 
                        };
                    })
                    .ToList();

                var invalidSeats = seatPreviews.Where(s => s.BaseFare <= 0).Select(s => s.SeatCode).ToList();
                if (invalidSeats.Any())
                    throw new Exception($"Seat pricing data is missing for seat(s): {string.Join(", ", invalidSeats)}. Ensure the frontend sends BaseFare and ExternalGst from the SRDV Seat Layout response.");

                var dummyBus = new BusBooking
                {
                    FromCity = request.FromCity,
                    ToCity = request.ToCity,
                    DepartureTime = string.IsNullOrWhiteSpace(request.DepartureTime) ? DateTime.UtcNow.AddDays(1) : DateTime.Parse(request.DepartureTime).ToUniversalTime(),
                    OperatorName = request.OperatorName ?? "Unknown",
                    BusType = request.BusType ?? "Unknown",
                    PriceInr = request.TotalFare,
                    GstCategory = "AC"
                };

                var pricing = await _promotionEngine.CalculateAsync(
                    dummyBus,
                    seatPreviews,
                    request.CouponCode,
                    request.PromotionId,
                    parsedUserId,
                    request.SelectedFeaturedOfferId);

                return Ok(pricing);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("book")]
        public async Task<IActionResult> BookBus([FromBody] CreateBusBookingRequestDto request)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to continue booking.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            var passengerValidationError = ValidateAndNormalizePassengers(request.Passengers, out var normalizedPassengers);
            if (passengerValidationError is not null)
                return BadRequest(passengerValidationError);

            if (string.IsNullOrWhiteSpace(request.PassengerPhone))
                return BadRequest("PassengerPhone is required for contact.");

            var contactName = string.IsNullOrWhiteSpace(request.PassengerName)
                ? normalizedPassengers![0].FullName
                : request.PassengerName.Trim();

            if (string.IsNullOrWhiteSpace(contactName))
                return BadRequest("PassengerName is required for contact.");

            var seatsRequired = normalizedPassengers!.Count;
            var strategy = dbContext.Database.CreateExecutionStrategy();

            try
            {
                var executionResult = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync();
                    try
                    {
                        if (string.IsNullOrWhiteSpace(request.FromCity) || string.IsNullOrWhiteSpace(request.ToCity) || string.IsNullOrWhiteSpace(request.DepartureTime))
                            throw new Exception("Missing required bus details in request payload.");

                        var depTime = DateTime.Parse(request.DepartureTime).ToUniversalTime();
                        var arrTime = string.IsNullOrWhiteSpace(request.ArrivalTime) ? depTime.AddHours(10) : DateTime.Parse(request.ArrivalTime).ToUniversalTime();

                        if (depTime <= DateTime.UtcNow)
                            throw new Exception("Cannot book a bus that already departed.");

                        // Create the BusBooking record just-in-time for this specific booking
                        var bus = new BusBooking
                        {
                            BusNumber = "SRDV-" + Random.Shared.Next(1000, 9999),
                            OperatorName = request.OperatorName ?? "Unknown",
                            BusType = request.BusType ?? "Unknown",
                            GstCategory = "AC",
                            FromCity = request.FromCity,
                            ToCity = request.ToCity,
                            DepartureTime = depTime,
                            ArrivalTime = arrTime,
                            PriceInr = request.TotalFare,
                            TotalSeats = 40,
                            AvailableSeats = 40,
                            BoardingPoint = request.BoardingPointName ?? "Default Point",
                            DroppingPoint = request.DroppingPointName ?? "Default Point",
                            TraceId = request.TraceId,
                            ResultIndex = request.ResultIndex,
                            SrdvIndex = request.SrdvIndex,
                            OperatorId = "",
                            CancellationPoliciesJson = null,
                            IsIdProofRequired = false
                        };

                        dbContext.BusBookings.Add(bus);
                        await dbContext.SaveChangesAsync();

                        var requestedSeatCodes = normalizedPassengers
                            .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                            .Select(x => x.SeatNumber!)
                            .ToList();

                        if (requestedSeatCodes.Count != requestedSeatCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                            throw new Exception("Duplicate seat numbers in request.");

                        if (requestedSeatCodes.Count != seatsRequired)
                        {
                            throw new Exception("Seat selection is mandatory for all passengers.");
                        }

                        // ========================================
                        // UNIFIED PROMOTION VALIDATION
                        // ========================================
                        BusPromotion? promotionToApply = null;
                        FeaturedOffer? selectedFeaturedOffer = null;

                        if (request.SelectedFeaturedOfferId.HasValue)
                        {
                            selectedFeaturedOffer = await dbContext.FeaturedOffers
                                .Include(x => x.Conditions)
                                .FirstOrDefaultAsync(x => x.Id == request.SelectedFeaturedOfferId.Value && x.IsActive);

                            if (selectedFeaturedOffer == null)
                                throw new Exception("Selected offer is invalid or inactive.");

                            if (!string.IsNullOrWhiteSpace(request.CouponCode))
                            {
                                throw new Exception("Featured offers cannot stack with manual coupons.");
                            }
                            if (request.PromotionId.HasValue)
                            {
                                throw new Exception("Only one manual promotion/offer can be applied.");
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(request.CouponCode))
                        {
                            var normalizedCoupon = request.CouponCode.Trim().ToUpperInvariant();
                            var promoByCode = await dbContext.BusPromotions
                                .Include(x => x.Conditions)
                                .FirstOrDefaultAsync(x => x.Code == normalizedCoupon && x.IsActive && !x.IsAutoApply);

                            if (promoByCode == null)
                            {
                                throw new Exception("Invalid or inactive coupon code.");
                            }

                            if (request.PromotionId.HasValue && request.PromotionId.Value != promoByCode.Id)
                            {
                                throw new Exception("Only one manual promotion/offer can be applied.");
                            }

                            promotionToApply = promoByCode;
                        }
                        else if (request.PromotionId.HasValue)
                        {
                            var promoById = await dbContext.BusPromotions
                                .Include(x => x.Conditions)
                                .FirstOrDefaultAsync(x => x.Id == request.PromotionId.Value && x.IsActive && !x.IsAutoApply);

                            if (promoById == null)
                            {
                                throw new Exception("Invalid or inactive promotion.");
                            }

                            promotionToApply = promoById;
                        }

                        if (selectedFeaturedOffer != null)
                        {
                            var nowUtc = DateTime.UtcNow;

                            if (selectedFeaturedOffer.StartDateUtc.HasValue && selectedFeaturedOffer.StartDateUtc.Value > nowUtc)
                            {
                                throw new Exception("Featured offer has not started yet.");
                            }

                            if (selectedFeaturedOffer.EndDateUtc.HasValue && selectedFeaturedOffer.EndDateUtc.Value < nowUtc)
                            {
                                throw new Exception("Featured offer has expired.");
                            }

                            if (selectedFeaturedOffer.MaxUsage.HasValue && selectedFeaturedOffer.UsedCount >= selectedFeaturedOffer.MaxUsage.Value)
                            {
                                throw new Exception("Featured offer usage limit reached.");
                            }
                        }
                        else if (promotionToApply != null)
                        {
                            var nowUtc = DateTime.UtcNow;

                            if (promotionToApply.StartDateUtc.HasValue && promotionToApply.StartDateUtc.Value > nowUtc)
                            {
                                throw new Exception("Promotion has not started yet.");
                            }

                            if (promotionToApply.EndDateUtc.HasValue && promotionToApply.EndDateUtc.Value < nowUtc)
                            {
                                throw new Exception("Promotion has expired.");
                            }

                            if (promotionToApply.MaxUsage.HasValue && promotionToApply.UsedCount >= promotionToApply.MaxUsage.Value)
                            {
                                throw new Exception("Promotion usage limit reached.");
                            }

                            if (promotionToApply.MaxUsagePerUser > 0)
                            {
                                var userPromoUsageCount = await dbContext.BusPromotionUsages
                                    .CountAsync(x => x.BusPromotionId == promotionToApply.Id && x.UserId == userId && x.BookingStatus == "Booked");

                                if (userPromoUsageCount >= promotionToApply.MaxUsagePerUser)
                                {
                                    throw new Exception("Your usage limit for this promotion has been reached.");
                                }
                            }

                            if (promotionToApply.IsFirstTimeUserOnly)
                            {
                                var userBookingHistoryService = HttpContext.RequestServices.GetRequiredService<IUserBookingHistoryService>();
                                var hasPrior = await userBookingHistoryService.HasPriorBookingAsync(userId, request.PassengerPhone);
                                if (hasPrior)
                                {
                                    throw new Exception("This promotion is only valid for your first booking.");
                                }
                            }
                        }

                        // ========================================
                        // CENTRALIZED PRICING ENGINE
                        // ========================================
                        var traceId = request.TraceId ?? string.Empty;
                        var blockedSeats = await dbContext.BusBlockedSeatPrices
                            .Where(x => x.TraceId == traceId)
                            .ToListAsync();

                        var seatPreviews = request.Passengers
                            .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                            .Select(p => {
                                // Match using case-insensitive check in case frontend modified case, but frontend is instructed not to modify it.
                                var blockedSeat = blockedSeats.FirstOrDefault(b => b.SeatName.Equals(p.SeatNumber, StringComparison.OrdinalIgnoreCase));
                                return new PickNBook.Api.Models.DTOs.SeatPreviewDto 
                                { 
                                    SeatCode = p.SeatNumber!, 
                                    BaseFare = blockedSeat?.BaseFare > 0 ? blockedSeat.BaseFare : p.BaseFare, 
                                    SeatType = p.SeatType,
                                    ExternalGst = blockedSeat?.GstAmount > 0 ? blockedSeat.GstAmount : p.ExternalGst 
                                };
                            })
                            .ToList();

                        var pricing = await _promotionEngine.CalculateAsync(
                            bus,
                            seatPreviews,
                            request.CouponCode,
                            request.PromotionId,
                            int.Parse(userId!),
                            request.SelectedFeaturedOfferId);

                        var pnr = await GenerateUniqueBusPnrAsync();
                        var reservation = new BusReservation
                        {
                            BookingReference = $"BS-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                            Pnr = pnr,
                            UserId = userId!,
                            BusBookingId = bus.Id,
                            PassengerName = contactName,
                            PassengerPhone = request.PassengerPhone.Trim(),
                            PassengerEmail = string.IsNullOrWhiteSpace(request.PassengerEmail) ? null : request.PassengerEmail.Trim(),
                            SeatsBooked = seatsRequired,
                            TotalPriceInr = pricing.GrandTotal,
                            CustomerFareInr = pricing.GrandTotal,
                            NetFareInr = pricing.SubtotalBeforeCoupon,
                            BaseFareInr = pricing.Seats.Sum(x => x.BaseFare),
                            MarkupAmountInr = pricing.Seats.Sum(x => x.MarkupAmount),
                            TaxableFareInr = pricing.TaxableFare,
                            GstPercent = pricing.GstPercent,
                            GstAmountInr = pricing.GstAmount,
                            DiscountAmountInr = pricing.AutoDiscountAmount + pricing.CouponDiscountAmount + pricing.ManualDiscountAmount,
                            ConvenienceFeeInr = pricing.ConvenienceFee,
                            CouponCode = string.IsNullOrWhiteSpace(request.CouponCode) ? null : request.CouponCode.Trim().ToUpperInvariant(),
                            AppliedPromotionId = promotionToApply?.Id,
                            AppliedPromotionCode = pricing.AppliedPromotionCode,
                            AppliedPromotionType = pricing.AppliedPromotionType ?? pricing.DiscountSource,
                            AppliedFeaturedOfferId = request.SelectedFeaturedOfferId,
                            AppliedFeaturedOfferTitle = selectedFeaturedOffer?.Title,
                            FeaturedOfferDiscountAmount = selectedFeaturedOffer != null ? pricing.ManualDiscountAmount : 0m,
                            AutoPromotionId = pricing.AutoPromotionCode != null ? (await dbContext.BusPromotions.Where(x => x.Code == pricing.AutoPromotionCode).Select(x => (int?)x.Id).FirstOrDefaultAsync()) : null,
                            AutoPromotionCode = pricing.AutoPromotionCode,
                            DiscountSource = pricing.DiscountSource,
                            Status = "Booked",
                            BookedAtUtc = DateTime.UtcNow,
                            
                            BoardingPointName = request.BoardingPointName,
                            BoardingPointTime = request.BoardingPointTime,
                            DroppingPointName = request.DroppingPointName,
                            DroppingPointTime = request.DroppingPointTime
                        };

                        dbContext.BusReservations.Add(reservation);
                        await dbContext.SaveChangesAsync();

                        // ========================================
                        // ATOMIC INCREMENT & USAGE LOGGING
                        // ========================================
                        if (pricing.AutoDiscountAmount > 0 && !string.IsNullOrEmpty(pricing.AutoPromotionCode))
                        {
                            var autoPromo = await dbContext.BusPromotions.FirstOrDefaultAsync(x => x.Code == pricing.AutoPromotionCode);
                            if (autoPromo != null)
                            {
                                var autoUsage = new BusPromotionUsage
                                {
                                    BusPromotionId = autoPromo.Id,
                                    FeaturedOfferId = null,
                                    BusReservationId = reservation.Id,
                                    UserId = userId!,
                                    PromotionCode = autoPromo.Code,
                                    PromotionType = autoPromo.PromotionType,
                                    DiscountAmountInr = pricing.AutoDiscountAmount,
                                    BookingTotalInr = pricing.GrandTotal,
                                    BookingStatus = "Booked",
                                    UsedAtUtc = DateTime.UtcNow
                                };
                                dbContext.BusPromotionUsages.Add(autoUsage);

                                var autoRows = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                    UPDATE buspromotions
                                    SET UsedCount = UsedCount + 1
                                    WHERE Id = {autoPromo.Id}
                                    AND (MaxUsage IS NULL OR UsedCount < MaxUsage)
                                ");
                                if (autoRows == 0 && autoPromo.MaxUsage.HasValue)
                                {
                                    throw new Exception($"Auto-promotion '{autoPromo.Code}' usage limit reached concurrently.");
                                }
                            }
                        }

                        if (selectedFeaturedOffer != null)
                        {
                            var featuredUsage = new FeaturedOfferUsage
                            {
                                FeaturedOfferId = selectedFeaturedOffer.Id,
                                BusReservationId = reservation.Id,
                                UserId = userId!,
                                DiscountAmount = pricing.ManualDiscountAmount,
                                UsedAtUtc = DateTime.UtcNow
                            };
                            dbContext.FeaturedOfferUsages.Add(featuredUsage);

                            var rowsUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE featuredoffers
                                SET UsedCount = UsedCount + 1
                                WHERE Id = {selectedFeaturedOffer.Id}
                                AND (MaxUsage IS NULL OR UsedCount < MaxUsage)
                            ");
                            if (rowsUpdated == 0 && selectedFeaturedOffer.MaxUsage.HasValue)
                            {
                                throw new Exception("Featured offer usage limit reached concurrently.");
                            }
                        }
                        else if (promotionToApply != null)
                        {
                            var discountAmt = promotionToApply.PromotionType.Equals("Coupon", StringComparison.OrdinalIgnoreCase)
                                ? pricing.CouponDiscountAmount
                                : pricing.ManualDiscountAmount;

                            var manualUsage = new BusPromotionUsage
                            {
                                BusPromotionId = promotionToApply.Id,
                                BusReservationId = reservation.Id,
                                UserId = userId!,
                                PromotionCode = promotionToApply.Code,
                                PromotionType = promotionToApply.PromotionType,
                                DiscountAmountInr = discountAmt,
                                BookingTotalInr = pricing.GrandTotal,
                                BookingStatus = "Booked",
                                UsedAtUtc = DateTime.UtcNow
                            };
                            dbContext.BusPromotionUsages.Add(manualUsage);

                            var manualRows = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE buspromotions
                                SET UsedCount = UsedCount + 1
                                WHERE Id = {promotionToApply.Id}
                                AND (MaxUsage IS NULL OR UsedCount < MaxUsage)
                            ");
                            if (manualRows == 0 && promotionToApply.MaxUsage.HasValue)
                            {
                                throw new Exception("Promotion usage limit reached concurrently.");
                            }
                        }

                        var passengers = new List<BusReservationPassenger>();
                        foreach (var p in normalizedPassengers)
                        {
                            passengers.Add(new BusReservationPassenger
                            {
                                BusReservationId = reservation.Id,
                                FullName = p.FullName,
                                Gender = p.Gender,
                                SeatNumber = p.SeatNumber!,
                                BaseFareInr = p.BaseFare,
                                SeatType = p.SeatType,
                                Age = p.Age
                            });
                        }
                        dbContext.BusReservationPassengers.AddRange(passengers);

                        await TrackBusRouteBookingCounterAsync(bus.FromCity, bus.ToCity);

                        await dbContext.SaveChangesAsync();

                        // ========================================
                        // DEBIT WALLET BEFORE EXTERNAL API CALL
                        // ========================================
                        if (User?.IsInRole(AuthRoles.Agent) == true && string.Equals(request.PaymentMethod, "Agent Wallet", StringComparison.OrdinalIgnoreCase))
                        {
                            var walletService = HttpContext.RequestServices.GetRequiredService<IAgentWalletService>();
                            await walletService.DebitWalletForBookingAsync(
                                int.Parse(userId!),
                                reservation.TotalPriceInr,
                                reservation.BookingReference,
                                "Bus",
                                $"Bus Booking - {bus.FromCity} to {bus.ToCity} ({bus.OperatorName}) - Ref: {reservation.BookingReference}"
                            );
                        }

                        if (bus.BusNumber.StartsWith("SRDV-") && !string.IsNullOrEmpty(bus.TraceId))
                        {
                            var srdvReq = new SrdvBusBookingRequestDto
                            {
                                TraceId = !string.IsNullOrWhiteSpace(request.TraceId) ? request.TraceId : bus.TraceId,
                                ResultIndex = !string.IsNullOrWhiteSpace(request.ResultIndex) ? request.ResultIndex : bus.ResultIndex!,
                                SrdvIndex = request.SrdvIndex > 0 ? request.SrdvIndex : (bus.SrdvIndex ?? 0),
                                BoardingPointId = request.BoardingPointId ?? bus.BoardingPoint,
                                DroppingPointId = request.DroppingPointId ?? bus.DroppingPoint,
                                Passengers = passengers.Select(p => new SrdvBusPassengerDto
                                {
                                    Title = p.Gender == "Male" ? "Mr" : "Ms",
                                    FirstName = p.FullName,
                                    LastName = "Passenger",
                                    Age = p.Age,
                                    Gender = p.Gender == "Male" ? 1 : 2,
                                    SeatName = p.SeatNumber,
                                    Fare = pricing.Seats.FirstOrDefault(s => s.SeatCode == p.SeatNumber)?.BaseFare ?? bus.PriceInr,
                                    Address = "PickNBook Address",
                                    City = bus.FromCity,
                                    State = "State",
                                    ContactNo = reservation.PassengerPhone,
                                    Email = reservation.PassengerEmail ?? "info@picknbook.com"
                                }).ToList()
                            };

                            var srdvRes = await _srdvBusService.BookBusAsync(srdvReq, request.BlockKey ?? "");
                            if (!srdvRes.Success)
                            {
                                throw new Exception($"SRDV Booking Failed: {srdvRes.ErrorMessage}");
                            }

                            reservation.SrdvBookingId = srdvRes.SrdvBookingId;
                            reservation.SrdvBookingResponseJson = srdvRes.ResponseJson;
                            reservation.SrdvTicketNo = srdvRes.TicketNo;
                            reservation.Pnr = srdvRes.TravelOperatorPNR ?? srdvRes.TicketNo ?? reservation.Pnr;
                            
                            // Save the actual TraceId used during booking
                            bus.TraceId = srdvReq.TraceId;

                            await dbContext.SaveChangesAsync();
                        }

                        await transaction.CommitAsync();




                        return new
                        {
                            Reservation = reservation,
                            Bus = bus,
                            Passengers = passengers,
                            Response = MapBusReservation(reservation, bus, passengers)
                        };
                    }

                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });

               

                try
                {
                    var reservationId = executionResult.Reservation.Id;
                    var targetBusId = executionResult.Bus.Id;
                    var backgroundJobQueue = HttpContext.RequestServices.GetRequiredService<IBackgroundJobQueue>();
                    backgroundJobQueue.QueueBackgroundWorkItem(async (sp, ct) =>
                    {
                        var scopedContext = sp.GetRequiredService<AppDbContext>();
                        var scopedEmail = sp.GetRequiredService<ITicketEmailService>();
                        var scopedWhatsApp = sp.GetRequiredService<IWhatsAppService>();
                        var scopedLogger = sp.GetRequiredService<ILogger<BusBookingsController>>();
                        var scopedSrdvBusService = sp.GetRequiredService<ISrdvBusService>();

                        var res = await scopedContext.BusReservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct);
                        var b = await scopedContext.BusBookings.FirstOrDefaultAsync(x => x.Id == targetBusId, ct);
                        var passengers = await scopedContext.BusReservationPassengers.Where(p => p.BusReservationId == reservationId).ToListAsync(ct);

                        if (res != null && b != null)
                        {
                            var seatNumbers = string.Join(", ",
                                passengers.Select(x => x.SeatNumber).Where(x => !string.IsNullOrWhiteSpace(x)));

                            if (string.IsNullOrWhiteSpace(seatNumbers))
                                seatNumbers = "N/A";

                            // Email notification
                            if (!string.IsNullOrWhiteSpace(res.PassengerEmail))
                            {
                                try
                                {
                                    await scopedEmail.SendBusTicketAsync(new SendBusTicketEmailRequest
                                    {
                                        ToEmail = res.PassengerEmail,
                                        PassengerName = res.PassengerName,
                                        BookingReference = res.BookingReference,
                                        Pnr = res.Pnr,
                                        OperatorName = b.OperatorName,
                                        BusType = b.BusType,
                                        Origin = scopedSrdvBusService.MapCityCodeToName(b.FromCity),
                                        Destination = scopedSrdvBusService.MapCityCodeToName(b.ToCity),
                                        DepartureTime = b.DepartureTime,
                                        ArrivalTime = b.ArrivalTime,
                                        IsOvernightArrival = b.ArrivalTime.Date > b.DepartureTime.Date,
                                        DurationMinutes = (int)(b.ArrivalTime - b.DepartureTime).TotalMinutes,
                                        BoardingPoint = !string.IsNullOrWhiteSpace(res.BoardingPointName) ? res.BoardingPointName : b.BoardingPoint,
                                        BoardingPointTime = res.BoardingPointTime ?? b.DepartureTime,
                                        ArrivalPoint = !string.IsNullOrWhiteSpace(res.DroppingPointName) ? res.DroppingPointName : b.ToCity,
                                        ArrivalPointTime = res.DroppingPointTime ?? b.ArrivalTime,
                                        Price = res.TotalPriceInr,
                                        BaseFare = res.BaseFareInr,
                                        Currency = "INR",
                                        NetFare = res.NetFareInr,
                                        AppliedPromotionCode = res.AppliedPromotionCode,
                                        AppliedPromotionType = res.AppliedPromotionType,
                                        DiscountSource = res.DiscountSource,
                                        DiscountAmount = res.DiscountAmountInr > 0 ? res.DiscountAmountInr : null,
                                        SeatNumber = seatNumbers,
                                        GstPercent = res.GstPercent,
                                        GstAmount = res.GstAmountInr,
                                        AutoDiscountAmount = res.AutoDiscountAmountInr,
                                        CouponDiscountAmount = res.CouponDiscountAmountInr,
                                        Passengers = passengers.Select(p => new BusPassengerSeatDto
                                        {
                                            FullName = p.FullName,
                                            Gender = p.Gender,
                                            SeatNumber = p.SeatNumber ?? string.Empty
                                        }).ToList(),
                                        CancellationPoliciesJson = b.CancellationPoliciesJson
                                    });
                                }
                                catch (Exception ex)
                                {
                                    scopedLogger.LogError(ex, "Background booking email failed for {BookingReference}", res.BookingReference);
                                }
                            }

                            // WhatsApp notification
                            try
                            {
                                var message = $@"
                Booking Confirmed ✅

                Ref: {res.BookingReference}
                Route: {b.FromCity} → {b.ToCity}
                Seats: {seatNumbers}
                Departure: {b.DepartureTime}
                ";

                                var whatsAppResult = await scopedWhatsApp.SendTextAsync(
                                    res.PassengerPhone,
                                    message
                                );

                                if (!whatsAppResult.IsSent)
                                    scopedLogger.LogWarning("Background WhatsApp booking failed: {Message}", whatsAppResult.Message);
                            }
                            catch (Exception ex)
                            {
                                scopedLogger.LogWarning(ex, "Background WhatsApp booking notification threw an error");
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to queue booking notifications for {BookingReference}",
                        executionResult.Reservation.BookingReference);
                }
                return CreatedAtAction(
     nameof(GetBusBookingById),
     new { bookingId = executionResult.Reservation.Id },
     executionResult.Response
 );

            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("bookings")]
        public async Task<IActionResult> GetBusBookings([FromQuery] string? passengerPhone, [FromQuery] string? status)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to continue booking.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            var queryable = dbContext.BusReservations
                .AsNoTracking()
                .Include(x => x.BusBooking)
                .Where(x => x.UserId == userId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(passengerPhone))
            {
                var phone = passengerPhone.Trim();
                queryable = queryable.Where(x => EF.Functions.Like(x.PassengerPhone, phone));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = status.Trim().ToLower();

                if (normalizedStatus == "all")
                {
                    // Do not apply any filter
                }
                else if (normalizedStatus == "upcoming")
                {
                    queryable = queryable.Where(x => x.Status == "Booked" && x.BusBooking.DepartureTime > DateTime.UtcNow);
                }
                else if (normalizedStatus == "completed" || normalizedStatus == "past")
                {
                    queryable = queryable.Where(x => x.Status == "Booked" && x.BusBooking.DepartureTime <= DateTime.UtcNow);
                }
                else if (normalizedStatus == "cancelled")
                {
                    queryable = queryable.Where(x => x.Status == "Cancelled");
                }
                else
                {
                    // Fallback for explicitly stored statuses like "Booked"
                    queryable = queryable.Where(x => EF.Functions.Like(x.Status, status.Trim()));
                }
            }

            var bookings = await queryable
                .OrderByDescending(x => x.BookedAtUtc)
                .Take(200)
                .ToListAsync();

            var bookingIds = bookings.Select(x => x.Id).ToList();
            var passengers = await dbContext.BusReservationPassengers
                .AsNoTracking()
                .Where(x => bookingIds.Contains(x.BusReservationId))
                .OrderBy(x => x.Id)
                .ToListAsync();

            var passengersByBooking = passengers
                .GroupBy(x => x.BusReservationId)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<BusReservationPassenger>)x.ToList());

            var response = bookings
                .Where(x => x.BusBooking is not null)
                .Select(x =>
                {
                    if (!passengersByBooking.TryGetValue(x.Id, out var passengerRows))
                    {
                        passengerRows = Array.Empty<BusReservationPassenger>();
                    }

                    return MapBusReservation(x, x.BusBooking!, passengerRows);
                });

            return Ok(response);
        }

        

        [HttpGet("bookings/{bookingId:int}")]
        public async Task<IActionResult> GetBusBookingById(int bookingId)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to continue booking.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            var booking = await dbContext.BusReservations
                .AsNoTracking()
                .Include(x => x.BusBooking)
                .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

            if (booking is null || booking.BusBooking is null)
            {
                return NotFound("Booking not found.");
            }

            var passengers = await dbContext.BusReservationPassengers
                .AsNoTracking()
                .Where(x => x.BusReservationId == booking.Id)
                .OrderBy(x => x.Id)
                .ToListAsync();

            return Ok(MapBusReservation(booking, booking.BusBooking, passengers));
        }


        [HttpPost("bookings/{bookingId}/cancel")]
        public async Task<IActionResult> CancelBusBooking(int bookingId, [FromQuery] string? reason)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to continue booking.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            var strategy = dbContext.Database.CreateExecutionStrategy();
            try
            {
                var executionResult = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync();

                    var booking = await dbContext.BusReservations
                        .Include(x => x.BusBooking)
                        .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

                    if (booking is null || booking.BusBooking is null)
                        throw new Exception("Booking not found.");

                    if (booking.Status == "Cancelled")
                        throw new Exception("Already cancelled.");
                    // Prevent cancellation after departure
                    if (booking.BusBooking.DepartureTime <= DateTime.UtcNow)
                    {
                        throw new Exception("Cannot cancel ticket after bus departure.");
                    }

                    // 🔥 GET PASSENGERS
                    var passengers = await dbContext.BusReservationPassengers
                        .Where(x => x.BusReservationId == booking.Id)
                        .ToListAsync();

                    var activePassengers = passengers.Where(x => !x.IsCancelled).ToList();
                    if (activePassengers.Count == 0)
                        throw new Exception("Already cancelled.");

                    var seatNumbers = activePassengers
                        .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                        .Select(x => x.SeatNumber!)
                        .ToList();

                    decimal srdvCancellationCharge = 0m;
                    decimal srdvRefundAmount = 0m;

                    if (booking.BusBooking.BusNumber.StartsWith("SRDV-") && !string.IsNullOrEmpty(booking.BusBooking.TraceId))
                    {
                        if (seatNumbers.Any())
                        {
                            bool partialAllowed = false;
                            string actualTraceId = booking.BusBooking.TraceId ?? string.Empty;

                            if (!string.IsNullOrWhiteSpace(booking.SrdvBookingResponseJson))
                            {
                                try
                                {
                                    var j = System.Text.Json.JsonDocument.Parse(booking.SrdvBookingResponseJson);
                                    if (j.RootElement.TryGetProperty("PartialCancellationAllowed", out var pc))
                                    {
                                        partialAllowed = pc.ValueKind == System.Text.Json.JsonValueKind.String 
                                            ? pc.GetString()?.ToLower() == "true" 
                                            : pc.GetBoolean();
                                    }
                                }
                                catch { }
                            }

                            List<BusReservationPassenger> successfullyCancelledPassengers = new();
                            if (partialAllowed)
                            {
                                foreach (var p in activePassengers)
                                {
                                    if (string.IsNullOrWhiteSpace(p.SeatNumber)) continue;
                                    var cancelResult = await _srdvBusService.CancelTicketAsync(
                                        actualTraceId,
                                        p.SeatNumber,
                                        string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason.Trim());
                                    
                                    if (cancelResult.Success)
                                    {
                                        successfullyCancelledPassengers.Add(p);
                                        srdvCancellationCharge += cancelResult.CancellationCharge;
                                        srdvRefundAmount += cancelResult.RefundAmount;
                                    }
                                    else
                                    {
                                        throw new Exception($"SRDV Provider Error: {cancelResult.ErrorMessage}");
                                    }
                                }
                                
                                if (successfullyCancelledPassengers.Count == 0)
                                {
                                    throw new Exception("SRDV Provider failed to cancel the seats on their server.");
                                }
                                
                                activePassengers = successfullyCancelledPassengers;
                            }
                            else
                            {
                                var cancelResult = await _srdvBusService.CancelTicketAsync(
                                    actualTraceId,
                                    string.Join(",", seatNumbers),
                                    string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason.Trim());

                                if (!cancelResult.Success)
                                {
                                    throw new Exception($"SRDV Provider Error: {cancelResult.ErrorMessage}");
                                }
                                srdvCancellationCharge += cancelResult.CancellationCharge;
                                srdvRefundAmount += cancelResult.RefundAmount;
                            }
                        }
                    }

                    bool requiresManualReview = false;
                    if (srdvCancellationCharge == 0 && srdvRefundAmount == 0)
                    {
                        // DO NOT fabricate refunds. If both are 0, the SRDV response was likely ambiguous.
                        requiresManualReview = true;
                    }


                    // Mark passengers cancelled
                    foreach (var p in activePassengers)
                    {
                        p.IsCancelled = true;
                        p.CancelledAtUtc = DateTime.UtcNow;
                    }

                    // Check if all originally active passengers were successfully cancelled
                    var allCancelledSuccessfully = activePassengers.Count == seatNumbers.Count;

                    if (allCancelledSuccessfully)
                    {
                        // 🔥 UPDATE BOOKING
                        booking.Status = "Cancelled";
                        booking.CancelledAtUtc = DateTime.UtcNow;
                        booking.CancellationReason = string.IsNullOrWhiteSpace(reason)
                            ? "Cancelled by user"
                            : reason.Trim();

                        // ✅ 1. Update coupon usage status
                        var usage = await dbContext.BusCouponUsages
                            .FirstOrDefaultAsync(x => x.BusReservationId == booking.Id);

                        if (usage != null)
                        {
                            usage.BookingStatus = "Cancelled";
                            usage.UsedAtUtc = DateTime.UtcNow;
                        }

                        // ✅ 2. Decrease global coupon usage
                        if (!string.IsNullOrWhiteSpace(booking.CouponCode))
                        {
                            await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE bus_coupons
        SET UsedCount = CASE 
            WHEN UsedCount > 0 THEN UsedCount - 1 
            ELSE 0 
        END
        WHERE CouponCode = {booking.CouponCode}
    ");
                        }

                        // Unified promotion cancellation handling:
                        var unifiedUsages = await dbContext.BusPromotionUsages
                            .Where(x => x.BusReservationId == booking.Id && x.BookingStatus == "Booked")
                            .ToListAsync();

                        foreach (var u in unifiedUsages)
                        {
                            u.BookingStatus = "Cancelled";
                            u.UsedAtUtc = DateTime.UtcNow;

                            // Decrement atomic promotion usage
                            await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE buspromotions
                                SET UsedCount = CASE 
                                    WHEN UsedCount > 0 THEN UsedCount - 1 
                                    ELSE 0 
                                END
                                WHERE Id = {u.BusPromotionId}
                            ");
                        }
                    }

                    // 🔥 validation
                    if (booking.SeatsBooked <= 0)
                        throw new Exception("Invalid seat count in booking");

                    
                    // ── Dynamic SRDV Cancellation Policy ──
                    // ── Dynamic SRDV Cancellation Policy ──
                    var refundInput = new PickNBook.Api.Models.DTOs.RefundCalculationInput
                    {
                        OriginalCustomerPaid = booking.TotalPriceInr,
                        SupplierAmount = booking.NetFareInr,
                        MarkupAmount = booking.MarkupAmountInr,
                        DiscountAmount = booking.CouponDiscountAmountInr + booking.AutoDiscountAmountInr + booking.FeaturedOfferDiscountAmount,
                        ConvenienceFee = booking.ConvenienceFeeInr,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        SupplierRefundAmount = srdvRefundAmount
                    };

                    var calculatedRefund = refundCalculator.CalculateCustomerRefund(
                        refundInput,
                        refundMarkup: false,
                        refundConvenienceFee: false,
                        refundCoupon: false);

                    booking.CancellationChargeInr = calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                    booking.RefundAmountInr = calculatedRefund.FinalCustomerRefundAmount;

                    var cancellationAudit = new PickNBook.Api.Models.Entities.BookingCancellation
                    {
                        BookingType = "Bus",
                        BookingReference = booking.BookingReference,
                        UserId = booking.UserId,
                        CreatedAtUtc = DateTime.UtcNow,
                        OriginalCustomerPaid = booking.TotalPriceInr,
                        SupplierAmount = booking.NetFareInr,
                        MarkupAmount = booking.MarkupAmountInr,
                        ConvenienceFee = booking.ConvenienceFeeInr,
                        DiscountAmount = booking.CouponDiscountAmountInr + booking.AutoDiscountAmountInr + booking.FeaturedOfferDiscountAmount,
                        SupplierRefundAmount = srdvRefundAmount,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        MarkupRefunded = calculatedRefund.MarkupRefunded,
                        FeeRefunded = calculatedRefund.FeeRefunded,
                        CouponForfeited = calculatedRefund.CouponForfeited,
                        CustomerRefundAmount = calculatedRefund.FinalCustomerRefundAmount,
                        Status = "Pending"
                    };
                    dbContext.BookingCancellations.Add(cancellationAudit);
                    await dbContext.SaveChangesAsync();

                    if (requiresManualReview)
                    {
                        cancellationAudit.Status = "PendingReview";
                        await dbContext.SaveChangesAsync();
                    }
                    else if (calculatedRefund.FinalCustomerRefundAmount > 0)
                    {
                        var payment = await dbContext.Payments.FirstOrDefaultAsync(p => p.UserId == booking.UserId && p.BookingReferenceId == booking.Id && p.BookingType == "Bus");
                        if (payment != null && payment.CashfreeOrderId != null)
                        {
                            string refundId = $"REF-CANCEL-{booking.Id}-{cancellationAudit.Id}";
                            await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, calculatedRefund.FinalCustomerRefundAmount, refundId, "Bus Cancellation");
                            cancellationAudit.CashfreeRefundId = refundId;
                            cancellationAudit.Status = "RefundInitiated";
                            cancellationAudit.PaymentId = payment.Id;
                            await dbContext.SaveChangesAsync();
                        }
                    }
                    else
                    {
                        cancellationAudit.Status = "Completed";
                        await dbContext.SaveChangesAsync();
                    }
                    


                    await dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultPassengers = await dbContext.BusReservationPassengers
                        .AsNoTracking()
                        .Where(x => x.BusReservationId == booking.Id)
                        .OrderBy(x => x.Id)
                        .ToListAsync();

                    var mapped = MapBusReservation(booking, booking.BusBooking, resultPassengers);
                    return new { Result = mapped, CancelledIds = activePassengers.Select(x => x.Id).ToList(), RefundAmount = calculatedRefund.FinalCustomerRefundAmount };
                });

                await TrySendBusCancellationNotificationsAsync(bookingId, userId!, executionResult.CancelledIds, executionResult.RefundAmount);

                return Ok(executionResult.Result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("bookings/{bookingId:int}/cancel-passengers")]
        public async Task<IActionResult> CancelBusPassengers(int bookingId, [FromBody] CancelPassengersRequestDto request)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to continue booking.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            if (request.PassengerIds == null || request.PassengerIds.Count == 0)
            {
                return BadRequest("PassengerIds are required.");
            }

            var strategy = dbContext.Database.CreateExecutionStrategy();
            try
            {
                var executionResult = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync();

                    var booking = await dbContext.BusReservations
                        .Include(x => x.BusBooking)
                        .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

                    if (booking is null || booking.BusBooking is null)
                        throw new Exception("Booking not found.");

                    if (booking.Status == "Cancelled")
                        throw new Exception("Already cancelled.");

                    // Prevent cancellation after departure
                    if (booking.BusBooking.DepartureTime <= DateTime.UtcNow)
                    {
                        throw new Exception("Cannot cancel ticket after bus departure.");
                    }

                    // 🔥 GET PASSENGERS
                    var passengers = await dbContext.BusReservationPassengers
                        .Where(x => x.BusReservationId == booking.Id)
                        .ToListAsync();

                    var targetPassengers = passengers
                        .Where(x => request.PassengerIds.Contains(x.Id))
                        .ToList();

                    if (targetPassengers.Count != request.PassengerIds.Count)
                        throw new Exception("One or more passenger IDs are invalid for this reservation.");

                    if (targetPassengers.Any(x => x.IsCancelled))
                        throw new Exception("One or more passenger tickets are already cancelled.");

                    var activePassengersCount = passengers.Count(x => !x.IsCancelled);
                    if (targetPassengers.Count > activePassengersCount)
                        throw new Exception("Cannot cancel more passengers than currently active.");

                    var seatNumbers = targetPassengers
                        .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                        .Select(x => x.SeatNumber!)
                        .ToList();

                    decimal srdvCancellationCharge = 0m;
                    decimal srdvRefundAmount = 0m;

                    if (booking.BusBooking.BusNumber.StartsWith("SRDV-") && !string.IsNullOrEmpty(booking.BusBooking.TraceId))
                    {
                        if (seatNumbers.Any())
                        {
                            bool partialAllowed = false;
                            if (!string.IsNullOrWhiteSpace(booking.SrdvBookingResponseJson))
                            {
                                try
                                {
                                    var j = System.Text.Json.JsonDocument.Parse(booking.SrdvBookingResponseJson);
                                    if (j.RootElement.TryGetProperty("PartialCancellationAllowed", out var pc))
                                    {
                                        partialAllowed = pc.ValueKind == System.Text.Json.JsonValueKind.String 
                                            ? pc.GetString()?.ToLower() == "true" 
                                            : pc.GetBoolean();
                                    }
                                }
                                catch { }
                            }

                            if (!partialAllowed)
                            {
                                throw new Exception("Partial cancellation is not permitted for this bus booking. You must cancel the entire booking.");
                            }

                            var isLeadPassengerCancelled = targetPassengers.Any(p => p.Id == passengers.First().Id);

                            if (isLeadPassengerCancelled && activePassengersCount > targetPassengers.Count)
                            {
                                throw new Exception("Cancelling the lead passenger will automatically cancel the entire booking on SRDV. If you want to cancel the entire booking, please use the Full Cancel button instead.");
                            }

                            List<BusReservationPassenger> successfullyCancelledPassengers = new();

                            foreach (var p in targetPassengers)
                            {
                                if (string.IsNullOrWhiteSpace(p.SeatNumber)) continue;
                                var cancelResult = await _srdvBusService.CancelTicketAsync(
                                    booking.BusBooking.TraceId,
                                    p.SeatNumber,
                                    "Partial passenger cancellation");
                                
                                if (cancelResult.Success)
                                {
                                    successfullyCancelledPassengers.Add(p);
                                    p.IsCancelled = true;
                                    p.CancelledAtUtc = DateTime.UtcNow;
                                    srdvCancellationCharge += cancelResult.CancellationCharge;
                                    srdvRefundAmount += cancelResult.RefundAmount;
                                }
                                else
                                {
                                    throw new Exception($"SRDV Provider Error: {cancelResult.ErrorMessage}");
                                }
                            }
                            
                            if (successfullyCancelledPassengers.Count == 0)
                            {
                                throw new Exception("SRDV Provider failed to cancel the selected seats on their server.");
                            }
                            
                            targetPassengers = successfullyCancelledPassengers;
                        }
                        else
                        {
                            // No seat numbers
                            foreach (var p in targetPassengers)
                            {
                                p.IsCancelled = true;
                                p.CancelledAtUtc = DateTime.UtcNow;
                            }
                        }
                    }

                    bool requiresManualReview = false;
                    if (booking.BusBooking.BusNumber.StartsWith("SRDV-") && srdvCancellationCharge == 0 && srdvRefundAmount == 0)
                    {
                        requiresManualReview = true;
                    }

                    // Non-SRDV booking logic is omitted above, so we handle refund based on defaults
                    else if (!booking.BusBooking.BusNumber.StartsWith("SRDV-"))
                    {
                        // Non-SRDV booking
                        foreach (var p in targetPassengers)
                        {
                            p.IsCancelled = true;
                            p.CancelledAtUtc = DateTime.UtcNow;
                        }
                    }

                    // ── Dynamic SRDV Cancellation Policy ──
                    decimal proportion = (decimal)targetPassengers.Count / (booking.SeatsBooked > 0 ? booking.SeatsBooked : 1);

                    var refundInput = new PickNBook.Api.Models.DTOs.RefundCalculationInput
                    {
                        OriginalCustomerPaid = booking.TotalPriceInr * proportion,
                        SupplierAmount = booking.NetFareInr * proportion,
                        MarkupAmount = booking.MarkupAmountInr * proportion,
                        DiscountAmount = (booking.CouponDiscountAmountInr + booking.AutoDiscountAmountInr + booking.FeaturedOfferDiscountAmount) * proportion,
                        ConvenienceFee = booking.ConvenienceFeeInr * proportion,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        SupplierRefundAmount = srdvRefundAmount
                    };

                    var calculatedRefund = refundCalculator.CalculateCustomerRefund(
                        refundInput,
                        refundMarkup: false,
                        refundConvenienceFee: false,
                        refundCoupon: false);

                    booking.CancellationChargeInr = (booking.CancellationChargeInr ?? 0m) + calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                    booking.RefundAmountInr = (booking.RefundAmountInr ?? 0m) + calculatedRefund.FinalCustomerRefundAmount;

                    var cancellationAudit = new PickNBook.Api.Models.Entities.BookingCancellation
                    {
                        BookingType = "Bus",
                        BookingReference = booking.BookingReference,
                        UserId = booking.UserId,
                        CreatedAtUtc = DateTime.UtcNow,
                        OriginalCustomerPaid = refundInput.OriginalCustomerPaid,
                        SupplierAmount = refundInput.SupplierAmount,
                        MarkupAmount = refundInput.MarkupAmount,
                        ConvenienceFee = refundInput.ConvenienceFee,
                        DiscountAmount = refundInput.DiscountAmount,
                        SupplierRefundAmount = srdvRefundAmount,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        MarkupRefunded = calculatedRefund.MarkupRefunded,
                        FeeRefunded = calculatedRefund.FeeRefunded,
                        CouponForfeited = calculatedRefund.CouponForfeited,
                        CustomerRefundAmount = calculatedRefund.FinalCustomerRefundAmount,
                        Status = "Pending"
                    };
                    dbContext.BookingCancellations.Add(cancellationAudit);
                    await dbContext.SaveChangesAsync();

                    if (requiresManualReview)
                    {
                        cancellationAudit.Status = "PendingReview";
                        await dbContext.SaveChangesAsync();
                    }
                    else if (calculatedRefund.FinalCustomerRefundAmount > 0)
                    {
                        var payment = await dbContext.Payments.FirstOrDefaultAsync(p => p.UserId == booking.UserId && p.BookingReferenceId == booking.Id && p.BookingType == "Bus");
                        if (payment != null && payment.CashfreeOrderId != null)
                        {
                            string refundId = $"REF-CANCEL-{booking.Id}-{cancellationAudit.Id}";
                            await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, calculatedRefund.FinalCustomerRefundAmount, refundId, "Bus Partial Cancellation");
                            cancellationAudit.CashfreeRefundId = refundId;
                            cancellationAudit.Status = "RefundInitiated";
                            cancellationAudit.PaymentId = payment.Id;
                            await dbContext.SaveChangesAsync();
                        }
                    }
                    else
                    {
                        cancellationAudit.Status = "Completed";
                        await dbContext.SaveChangesAsync();
                    }

                    // If all active passengers are now cancelled, set full booking status to Cancelled and cancel coupon/promo usages
                    var remainingActiveCount = activePassengersCount - targetPassengers.Count;
                    if (remainingActiveCount == 0)
                    {
                        booking.Status = "Cancelled";
                        booking.CancelledAtUtc = DateTime.UtcNow;
                        booking.CancellationReason = string.IsNullOrWhiteSpace(request.Reason)
                            ? "All passengers cancelled"
                            : request.Reason.Trim();

                        // ✅ 1. Update coupon usage status
                        var usage = await dbContext.BusCouponUsages
                            .FirstOrDefaultAsync(x => x.BusReservationId == booking.Id);

                        if (usage != null)
                        {
                            usage.BookingStatus = "Cancelled";
                            usage.UsedAtUtc = DateTime.UtcNow;
                        }

                        // ✅ 2. Decrease global coupon usage
                        if (!string.IsNullOrWhiteSpace(booking.CouponCode))
                        {
                            await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE bus_coupons
                                SET UsedCount = CASE 
                                    WHEN UsedCount > 0 THEN UsedCount - 1 
                                    ELSE 0 
                                END
                                WHERE CouponCode = {booking.CouponCode}
                            ");
                        }

                        // Unified promotion cancellation handling:
                        var unifiedUsages = await dbContext.BusPromotionUsages
                            .Where(x => x.BusReservationId == booking.Id && x.BookingStatus == "Booked")
                            .ToListAsync();

                        foreach (var u in unifiedUsages)
                        {
                            u.BookingStatus = "Cancelled";
                            u.UsedAtUtc = DateTime.UtcNow;

                            // Decrement atomic promotion usage
                            await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE buspromotions
                                SET UsedCount = CASE 
                                    WHEN UsedCount > 0 THEN UsedCount - 1 
                                    ELSE 0 
                                END
                                WHERE Id = {u.BusPromotionId}
                            ");
                        }
                    }



                    await dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultPassengers = await dbContext.BusReservationPassengers
                        .AsNoTracking()
                        .Where(x => x.BusReservationId == booking.Id)
                        .OrderBy(x => x.Id)
                        .ToListAsync();

                    var mapped = MapBusReservation(booking, booking.BusBooking, resultPassengers);

                    return new { Result = mapped, CancelledIds = targetPassengers.Select(x => x.Id).ToList(), RefundAmount = calculatedRefund.FinalCustomerRefundAmount };
                });

                await TrySendBusCancellationNotificationsAsync(bookingId, userId!, executionResult.CancelledIds, executionResult.RefundAmount);

                return Ok(executionResult.Result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private async Task<string> GenerateUniqueBusPnrAsync()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            while (true)
            {
                var pnr = new string(Enumerable.Repeat(chars, 8)
                    .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
                if (!await dbContext.BusReservations.AnyAsync(x => x.Pnr == pnr))
                {
                    return pnr;
                }
            }
        }

        private static object MapBusReservation(BusReservation reservation, BusBooking bus, IReadOnlyList<BusReservationPassenger> passengers)
        {
            var baseDto = new BookingResponseDto
            {
                BookingId = reservation.Id.ToString(),
                Id = reservation.Id,
                BookingReference = reservation.BookingReference,
                Pnr = reservation.Pnr,
                TripType = "Bus",
                TripId = bus.Id,
                TripNumber = bus.BusNumber,
                ProviderName = bus.OperatorName,
                FromCity = bus.FromCity,
                ToCity = bus.ToCity,
                DepartureTimeUtc = DateTime.SpecifyKind(bus.DepartureTime, DateTimeKind.Utc),
                ArrivalTimeUtc = DateTime.SpecifyKind(bus.ArrivalTime, DateTimeKind.Utc),
                Status = reservation.Status,
                PassengerName = reservation.PassengerName,
                PassengerPhone = reservation.PassengerPhone,
                PassengerEmail = reservation.PassengerEmail,
                TravelClass = "Not Applicable",
                Adults = reservation.SeatsBooked,
                Children = 0,
                Infants = 0,
                SeatsBooked = reservation.SeatsBooked,
                TotalPriceInr = reservation.TotalPriceInr,
                BookedAtUtc = DateTime.SpecifyKind(reservation.BookedAtUtc, DateTimeKind.Utc),
                CancelledAtUtc = reservation.CancelledAtUtc.HasValue ? DateTime.SpecifyKind(reservation.CancelledAtUtc.Value, DateTimeKind.Utc) : null,
                CancellationReason = reservation.CancellationReason
            };

            var passengerDtos = passengers.Select(x => new BusPassengerResponseDto
            {
                Id = x.Id,
                FullName = x.FullName,
                Gender = x.Gender,
                SeatNumber = x.SeatNumber ?? string.Empty,
                Age = x.Age,
                IsCancelled = x.IsCancelled,
                CancelledAtUtc = x.CancelledAtUtc.HasValue ? DateTime.SpecifyKind(x.CancelledAtUtc.Value, DateTimeKind.Utc) : null
            }).ToList();

            var maleCount = passengers.Count(x => x.Gender.Equals("Male", StringComparison.OrdinalIgnoreCase));
            var femaleCount = passengers.Count(x => x.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase));

            return new
            {
                baseDto.BookingId,
                baseDto.BookingReference,
                baseDto.Pnr,
                baseDto.TripType,
                baseDto.TripId,
                baseDto.TripNumber,
                baseDto.ProviderName,
                baseDto.FromCity,
                baseDto.ToCity,
                baseDto.DepartureTimeUtc,
                baseDto.ArrivalTimeUtc,
                baseDto.Status,
                CanCancel =
        reservation.Status == "Booked" &&
        bus.DepartureTime > DateTime.UtcNow,

                TripState =
        reservation.Status == "Cancelled"
            ? "Cancelled"
            : bus.DepartureTime <= DateTime.UtcNow
                ? "Completed"
                : "Upcoming",
                baseDto.PassengerName,
                baseDto.PassengerPhone,
                baseDto.PassengerEmail,
                baseDto.TravelClass,
                baseDto.Adults,
                baseDto.Children,
                baseDto.Infants,
                baseDto.SeatsBooked,
                baseDto.TotalPriceInr,
                reservation.CustomerFareInr,
                reservation.NetFareInr,
                reservation.DiscountAmountInr,
                reservation.AutoDiscountAmountInr,
                reservation.CouponDiscountAmountInr,
                reservation.ConvenienceFeeInr,
                reservation.BaseFareInr,

                reservation.MarkupAmountInr,

                //reservation.MarkupPercent,

                reservation.TaxableFareInr,

                reservation.GstPercent,

                reservation.GstAmountInr,
                reservation.AppliedPromotionId,
                reservation.AppliedPromotionCode,
                reservation.AppliedPromotionType,
                reservation.AppliedFeaturedOfferId,
                reservation.AppliedFeaturedOfferTitle,
                reservation.FeaturedOfferDiscountAmount,
                reservation.CouponCode,
                reservation.AutoPromotionCode,
                reservation.CancellationChargeInr,
                reservation.RefundAmountInr,
                baseDto.BookedAtUtc,
                baseDto.CancelledAtUtc,
                baseDto.CancellationReason,
                Passengers = passengerDtos,
                MaleCount = maleCount,
                FemaleCount = femaleCount
            };
        }

        private async Task IncrementBusRouteSearchCounterAsync(
            string fromCity,
            string toCity,
            string? userOrGuestId,
            DateOnly? journeyDate)
        {
            var stat = await dbContext.BusRouteStats
                .FirstOrDefaultAsync(x => x.FromCity == fromCity && x.ToCity == toCity);

            if (stat is null)
            {
                dbContext.BusRouteStats.Add(new BusRouteStat
                {
                    FromCity = fromCity,
                    ToCity = toCity,
                    SearchCount = 1,
                    BookingCount = 0,
                    LastSearchedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                stat.SearchCount += 1;
                stat.LastSearchedAtUtc = DateTime.UtcNow;
            }

            dbContext.BusSearchLogs.Add(new BusSearchLog
            {
                UserId = currentUserService.IsAuthenticated() ? userOrGuestId : null,
                UserOrGuestId = userOrGuestId,
                IsGuest = currentUserService.IsGuest(),
                FromCity = fromCity,
                ToCity = toCity,
                JourneyDate = journeyDate,
                SearchedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        private async Task TrackBusRouteBookingCounterAsync(string fromCity, string toCity)
        {
            var stat = await dbContext.BusRouteStats
                .FirstOrDefaultAsync(x => x.FromCity == fromCity && x.ToCity == toCity);

            if (stat is null)
            {
                dbContext.BusRouteStats.Add(new BusRouteStat
                {
                    FromCity = fromCity,
                    ToCity = toCity,
                    SearchCount = 0,
                    BookingCount = 1,
                    LastBookedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                stat.BookingCount += 1;
                stat.LastBookedAtUtc = DateTime.UtcNow;
            }
        }

        private async Task<decimal> GetActiveBusConvenienceFeeAsync()
        {
            var feeRow = await dbContext.BusConvenienceFees
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .FirstOrDefaultAsync(x => x.Status == "Active");

            return feeRow?.FeeInr ?? 0m;
        }
        private async Task<BusMarkupSetting?> GetActiveSeatMarkupAsync(string seatType)
        {
            var cacheKey = $"BusMarkup_{seatType.ToUpper()}";
            if (!cache.TryGetValue(cacheKey, out BusMarkupSetting? markup))
            {
                markup = await dbContext.BusMarkupSettings
                    .AsNoTracking()
                    .OrderByDescending(x => x.UpdateDateUtc)
                    .FirstOrDefaultAsync(x =>
                        x.Status == "Active" &&
                        x.SeatType.ToUpper() == seatType.ToUpper());

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                };
                
                cache.Set(cacheKey, markup, cacheOptions);
            }
            return markup;
        }
        private async Task<(BusMarkupSetting? Seater, BusMarkupSetting? Sleeper)> GetBothMarkupsAsync()
        {
            var cacheKey = "BusMarkup_BOTH";
            if (!cache.TryGetValue(cacheKey, out (BusMarkupSetting? Seater, BusMarkupSetting? Sleeper) result))
            {
                var allMarkups = await dbContext.BusMarkupSettings
                    .AsNoTracking()
                    .Where(x => x.Status == "Active")
                    .ToListAsync();

                result = (
                    allMarkups.FirstOrDefault(x => x.SeatType.Equals("Seater", StringComparison.OrdinalIgnoreCase)),
                    allMarkups.FirstOrDefault(x => x.SeatType.Equals("Sleeper", StringComparison.OrdinalIgnoreCase))
                );
                cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));
            }
            return result;
        }
        private async Task<BusGstSetting?> GetActiveBusGstAsync(
     string gstCategory)
        {
            return await dbContext.BusGstSettings
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .FirstOrDefaultAsync(x =>
                    x.Status == "Active" &&
                    x.GstCategory == gstCategory);
        }
        private static decimal CalculateCouponAmount(decimal baseFare, BusCoupon coupon)
        {
            var amount = coupon.CouponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                ? baseFare * (coupon.Value / 100m)
                : coupon.Value;

            if (amount < 0)
            {
                amount = 0;
            }

            if (amount > baseFare)
            {
                amount = baseFare;
            }

            return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        }

        private static string? ValidateAndNormalizePassengers(IReadOnlyList<CreateBusPassengerDto>? passengers, out List<CreateBusPassengerDto>? normalizedPassengers)
        {
            normalizedPassengers = null;

            if (passengers is null || passengers.Count == 0)
            {
                return "At least one passenger is required.";
            }

            normalizedPassengers = new List<CreateBusPassengerDto>();
            for (var i = 0; i < passengers.Count; i++)
            {
                var passenger = passengers[i];
                if (string.IsNullOrWhiteSpace(passenger.FullName))
                {
                    return $"Passenger at index {i} has invalid FullName.";
                }

                var normalizedGender = AllowedPassengerGenders.FirstOrDefault(x =>
                    x.Equals(passenger.Gender?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (normalizedGender is null)
                {
                    return $"Passenger at index {i} has invalid Gender. Allowed values: {string.Join(", ", AllowedPassengerGenders)}.";
                }

                var normalizedSeat = passenger.SeatNumber?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedSeat))
                {
                    return $"Passenger at index {i} must select a seat.";
                }

                if (passenger.Age <= 0 || passenger.Age > 120)
                {
                    return $"Passenger at index {i} has invalid Age.";
                }

                normalizedPassengers.Add(new CreateBusPassengerDto
                {
                    FullName = passenger.FullName.Trim(),
                    Gender = normalizedGender,
                    SeatNumber = normalizedSeat,
                    Age=passenger.Age
                });
            }

            return null;
        }

        private static (DateTime StartUtc, DateTime EndUtc) GetUtcRangeForIstDate(DateOnly date)
        {
            var startIst = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaOffset);
            var endIst = startIst.AddDays(1);

            var startUtcRaw = startIst.UtcDateTime;
            var endUtcRaw = endIst.UtcDateTime;

            // Return exact UTC range (no truncation) to match second-precision scheduling
            return (
                new DateTime(startUtcRaw.Ticks, DateTimeKind.Utc),
                new DateTime(endUtcRaw.Ticks, DateTimeKind.Utc)
            );
        }



        private static decimal CalculateMarkupAmount(
    decimal baseFare,
    BusMarkupSetting? markup)
        {
            if (markup == null)
                return 0m;

            if (markup.MarkupType.Equals(
                "Percentage",
                StringComparison.OrdinalIgnoreCase))
            {
                return baseFare * markup.Value / 100m;
            }

            // FIXED
            return markup.Value;
        }
    //    private async Task<BusPricingPreviewResponseDto> CalculateBusPricingAsync(
    //int busId,
    //List<string> seatCodes,
    //string? couponCode)
    //    {
    //        var bus = await dbContext.BusBookings
    //            .AsNoTracking()
    //            .FirstOrDefaultAsync(x => x.Id == busId);

    //        if (bus is null)
    //            throw new Exception("Bus not found.");

    //        var seats = await dbContext.BusSeats
    //            .AsNoTracking()
    //            .Where(x =>
    //                x.BusBookingId == busId &&
    //                seatCodes.Contains(x.SeatCode))
    //            .ToListAsync();

    //        var response = new BusPricingPreviewResponseDto
    //        {
    //            BusId = bus.Id,
    //            GstCategory = bus.GstCategory
    //        };

    //        decimal subtotal = 0m;

    //        foreach (var seat in seats)
    //        {
    //            var markup = await GetActiveSeatMarkupAsync(seat.SeatType);

    //            var markupAmount = CalculateMarkupAmount(
    //                bus.PriceInr,
    //                markup);

    //            var fareBeforeTax = bus.PriceInr + markupAmount;

    //            subtotal += fareBeforeTax;

    //            response.Seats.Add(new BusSeatPriceBreakdownDto
    //            {
    //                SeatCode = seat.SeatCode,
    //                SeatType = seat.SeatType,
    //                BaseFare = bus.PriceInr,

    //                MarkupAmount = decimal.Round(
    //                    markupAmount,
    //                    2,
    //                    MidpointRounding.AwayFromZero),

    //                FareBeforeTax = decimal.Round(
    //                    fareBeforeTax,
    //                    2,
    //                    MidpointRounding.AwayFromZero)
    //            });
    //        }

    //        response.SubtotalBeforeCoupon = decimal.Round(
    //            subtotal,
    //            2,
    //            MidpointRounding.AwayFromZero);

    //        decimal couponAmount = 0m;

    //        if (!string.IsNullOrWhiteSpace(couponCode))
    //        {
    //            var coupon = await dbContext.BusCoupons
    //                .FirstOrDefaultAsync(x =>
    //                    x.CouponCode == couponCode &&
    //                    x.Status == "Active");

    //            if (coupon is not null)
    //            {
    //                couponAmount =
    //coupon.CouponType.Equals(
    //    "Percentage",
    //    StringComparison.OrdinalIgnoreCase)
    //? subtotal * coupon.Value / 100m
    //: coupon.Value;
    //            }
    //        }

    //        couponAmount = Math.Min(couponAmount, subtotal);

    //        response.CouponAmount = decimal.Round(
    //            couponAmount,
    //            2,
    //            MidpointRounding.AwayFromZero);

    //        var taxableFare = subtotal - couponAmount;

    //        response.TaxableFare = decimal.Round(
    //            taxableFare,
    //            2,
    //            MidpointRounding.AwayFromZero);

    //        var gstSetting = await GetActiveBusGstAsync(
    //            bus.GstCategory);

    //        var gstPercent = gstSetting?.GstPercent ?? 0m;

    //        response.GstPercent = gstPercent;

    //        var gstAmount = taxableFare * gstPercent / 100m;

    //        response.GstAmount = decimal.Round(
    //            gstAmount,
    //            2,
    //            MidpointRounding.AwayFromZero);

    //        var convenienceFee =
    //            await GetActiveBusConvenienceFeeAsync();

    //        response.ConvenienceFee = convenienceFee;

    //        response.GrandTotal = decimal.Round(
    //            taxableFare +
    //            gstAmount +
    //            convenienceFee,
    //            2,
    //            MidpointRounding.AwayFromZero);

    //        return response;
    //    }
        private async Task<decimal> GetSeatFinalFareAsync(
            decimal baseFare,
            string seatType)
        {
            var markup = await GetActiveSeatMarkupAsync(seatType);

            var markupAmount = CalculateMarkupAmount(baseFare, markup);

            return decimal.Round(
                baseFare + markupAmount,
                2,
                MidpointRounding.AwayFromZero);
        }
        private static DateTime ToIst(DateTime utcDateTime)
        {
            return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc).Add(IndiaOffset);
        }

        private async Task GetOrCreateSrdvBusesInDbAsync(DateOnly date, string fromCity, string toCity)
        {
            var journeyDate = date.ToString("yyyy-MM-dd");
            List<SrdvBusOfferDto> srdvBuses = new();
            try
            {
                srdvBuses = await _srdvBusService.SearchBusesAsync(fromCity, toCity, journeyDate);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch buses from SRDV.");
                throw new Exception($"SRDV Provider failure: {ex.Message}");
            }

            await SyncSrdvBusesToDbAsync(srdvBuses, date, fromCity, toCity);
        }

        private async Task SyncSrdvBusesToDbAsync(List<SrdvBusOfferDto> srdvBuses, DateOnly date, string fromCity, string toCity)
        {
            if (!srdvBuses.Any())
            {
                return;
            }

            // Sync with DB
            foreach (var offer in srdvBuses)
            {
                if (!DateTime.TryParse(offer.DepartureTime, out var depTime) || !DateTime.TryParse(offer.ArrivalTime, out var arrTime))
                {
                    // Fallback formatting or skip
                    depTime = new DateTime(date.Year, date.Month, date.Day, 10, 0, 0, DateTimeKind.Utc);
                    arrTime = depTime.AddHours(10);
                }
                else
                {
                    // SRDV returns times in IST. Subtract 5.5 hours to convert to UTC before saving
                    depTime = DateTime.SpecifyKind(depTime.AddHours(-5.5), DateTimeKind.Utc);
                    arrTime = DateTime.SpecifyKind(arrTime.AddHours(-5.5), DateTimeKind.Utc);
                }

                bool alreadyExists = await dbContext.BusBookings.AnyAsync(x =>
                    x.OperatorName == offer.OperatorName &&
                    x.FromCity == fromCity &&
                    x.ToCity == toCity &&
                    x.DepartureTime == depTime);

                if (!alreadyExists)
                {
                    var bus = new BusBooking
                    {
                        BusNumber = "SRDV-" + Random.Shared.Next(1000, 9999),
                        OperatorName = offer.OperatorName,
                        BusType = offer.BusType,
                        GstCategory = "AC", // Simplified
                        FromCity = fromCity,
                        ToCity = toCity,
                        DepartureTime = depTime,
                        ArrivalTime = arrTime,
                        PriceInr = offer.Price,
                        TotalSeats = offer.AvailableSeats > 0 ? offer.AvailableSeats : 40,
                        AvailableSeats = offer.AvailableSeats,
                        BoardingPoint = fromCity,
                        DroppingPoint = toCity,
                        TraceId = offer.TraceId,
                        ResultIndex = offer.ResultIndex,
                        SrdvIndex = offer.SrdvIndex,
                        OperatorId = offer.OperatorId
                    };
                    dbContext.BusBookings.Add(bus);
                }
            }

            await dbContext.SaveChangesAsync();
        }

        // Removed EnsureBusSchedulesForDateAsync


       
        private Dictionary<string, (int row, int col, int sectionIndex)> BuildSeatGrid(
    List<SeatSection> sections)
        {
            var map = new Dictionary<string, (int, int, int)>();

            for (int s = 0; s < sections.Count; s++)
            {
                var section = sections[s];

                for (int i = 0; i < section.SeatCodes.Count; i++)
                {
                    var row = i / section.ColumnsPerRow;
                    var col = i % section.ColumnsPerRow;

                    map[section.SeatCodes[i]] = (row, col, s);
                }
            }

            return map;
        }

        private List<string> GetAdjacentSeats(
            string seatCode,
            Dictionary<string, (int row, int col, int sectionIndex)> grid,
            List<SeatSection> sections)
        {
            if (!grid.TryGetValue(seatCode, out var pos))
                return [];

            var (row, col, sectionIndex) = pos;
            var section = sections[sectionIndex];

            var result = new List<string>();

            // LEFT
            if (col > 0 && section.AisleAfterColumn != col - 1)
            {
                result.Add(section.SeatCodes[row * section.ColumnsPerRow + (col - 1)]);
            }

            // RIGHT
            if (col < section.ColumnsPerRow - 1 && section.AisleAfterColumn != col)
            {
                result.Add(section.SeatCodes[row * section.ColumnsPerRow + (col + 1)]);
            }

            return result;
        }





        
        private async Task TrySendBusCancellationNotificationsAsync(
            int bookingId,
            string userId,
            List<int> newlyCancelledPassengerIds,
            decimal currentRefundAmount)
        {
            var booking = await dbContext.BusReservations
                .Include(x => x.BusBooking)
                .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

            if (booking == null || booking.BusBooking == null)
                return;

            var passengers = await dbContext.BusReservationPassengers
                .Where(x => x.BusReservationId == booking.Id)
                .ToListAsync();

            var newlyCancelledPassengers = passengers
                .Where(x => newlyCancelledPassengerIds.Contains(x.Id))
                .ToList();

            var seatNumbers = string.Join(", ",
                newlyCancelledPassengers.Select(x => x.SeatNumber).Where(x => !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(seatNumbers))
                seatNumbers = "N/A";

            // ---------------- EMAIL ----------------
            if (!string.IsNullOrWhiteSpace(booking.PassengerEmail))
            {
                try
                {
                    await _ticketEmailService.SendBusCancellationAsync(
                        new SendBusTicketEmailRequest
                        {
                            ToEmail = booking.PassengerEmail,
                            PassengerName = booking.PassengerName,
                            BookingReference = booking.BookingReference,
                            Pnr = booking.Pnr,
                            OperatorName = booking.BusBooking.OperatorName,
                            BusType = booking.BusBooking.BusType,
                            Origin = _srdvBusService.MapCityCodeToName(booking.BusBooking.FromCity),
                            Destination = _srdvBusService.MapCityCodeToName(booking.BusBooking.ToCity),
                            DepartureTime = booking.BusBooking.DepartureTime,
                            ArrivalTime = booking.BusBooking.ArrivalTime,
                            IsOvernightArrival = booking.BusBooking.ArrivalTime.Date > booking.BusBooking.DepartureTime.Date,
                            DurationMinutes = (int)(booking.BusBooking.ArrivalTime - booking.BusBooking.DepartureTime).TotalMinutes,
                            BoardingPoint = booking.BusBooking.BoardingPoint,
                            ArrivalPoint = booking.BusBooking.ToCity,

                            // Fare breakdown
                            Price = booking.TotalPriceInr,
                            BaseFare = booking.BaseFareInr,
                            Currency = "INR",

                            NetFare = booking.NetFareInr,
                            GstPercent = booking.GstPercent,
                            GstAmount = booking.GstAmountInr,

                            AppliedPromotionCode = booking.AppliedPromotionCode,
                            AppliedPromotionType = booking.AppliedPromotionType,
                            DiscountSource = booking.DiscountSource,
                            DiscountAmount = booking.DiscountAmountInr > 0 ? booking.DiscountAmountInr : null,

                            // Legacy fallback
                            SeatNumber = seatNumbers,
                            AutoDiscountAmount = booking.AutoDiscountAmountInr,
                            CouponDiscountAmount = booking.CouponDiscountAmountInr,

                            // Per-passenger details
                            Passengers = newlyCancelledPassengers.Select(p => new BusPassengerSeatDto
                            {
                                FullName = p.FullName,
                                Gender = p.Gender,
                                SeatNumber = p.SeatNumber ?? string.Empty
                            }).ToList()
                        },
                        currentRefundAmount
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Cancellation email failed for {BookingReference}", booking.BookingReference);
                }
            }

            // ---------------- WHATSAPP ----------------
            var message = $@"
Booking Cancelled ❌

Ref: {booking.BookingReference}
Route: {booking.BusBooking.FromCity} → {booking.BusBooking.ToCity}
Seats: {seatNumbers}
Refund: ₹{currentRefundAmount}
";

            var (sent, msg) = await _whatsAppService.SendTextAsync(
                booking.PassengerPhone,
                message
            );

            if (!sent)
                logger.LogWarning("WhatsApp cancellation failed: {Message}", msg);
        }

        private async Task TrySendBusBookingNotificationsAsync(
     BusReservation reservation,
     BusBooking bus,
     IReadOnlyList<BusReservationPassenger> passengers)
        {
            var seatNumbers = string.Join(", ",
                passengers.Select(x => x.SeatNumber).Where(x => !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(seatNumbers))
                seatNumbers = "N/A";

            // ---------------- EMAIL ----------------
            if (!string.IsNullOrWhiteSpace(reservation.PassengerEmail))
            {
                try
                {
                    // Fetch coupon details for PDF
                    //BusCoupon? couponDetails = null;
                    //if (!string.IsNullOrWhiteSpace(reservation.CouponCode))
                    //{
                    //    couponDetails = await dbContext.BusCoupons
                    //        .AsNoTracking()
                    //        .FirstOrDefaultAsync(x => x.CouponCode == reservation.CouponCode);
                    //}

                    await _ticketEmailService.SendBusTicketAsync(new SendBusTicketEmailRequest
                    {
                        ToEmail = reservation.PassengerEmail,
                        PassengerName = reservation.PassengerName,
                        BookingReference = reservation.BookingReference,
                        Pnr = reservation.Pnr,
                        OperatorName = bus.OperatorName,
                        BusType = bus.BusType,
                        Origin = _srdvBusService.MapCityCodeToName(bus.FromCity),
                        Destination = _srdvBusService.MapCityCodeToName(bus.ToCity),
                        DepartureTime = bus.DepartureTime,
                        ArrivalTime = bus.ArrivalTime,
                        IsOvernightArrival = bus.ArrivalTime.Date > bus.DepartureTime.Date,
                        DurationMinutes = (int)(bus.ArrivalTime - bus.DepartureTime).TotalMinutes,
                        BoardingPoint = !string.IsNullOrWhiteSpace(reservation.BoardingPointName) ? reservation.BoardingPointName : bus.BoardingPoint,
                        BoardingPointTime = reservation.BoardingPointTime ?? bus.DepartureTime,
                        ArrivalPoint = !string.IsNullOrWhiteSpace(reservation.DroppingPointName) ? reservation.DroppingPointName : bus.ToCity,
                        ArrivalPointTime = reservation.DroppingPointTime ?? bus.ArrivalTime,

                        // Fare breakdown
                        Price = reservation.TotalPriceInr,
                        BaseFare = reservation.BaseFareInr,
                        Currency = "INR",
                        NetFare = reservation.NetFareInr,

                        AppliedPromotionCode =
    reservation.AppliedPromotionCode,

                        AppliedPromotionType =
    reservation.AppliedPromotionType,

                        DiscountSource =
    reservation.DiscountSource,

                        DiscountAmount =
    reservation.DiscountAmountInr > 0
        ? reservation.DiscountAmountInr
        : null,

                        // Legacy fallback
                        SeatNumber = seatNumbers,
                        GstPercent = reservation.GstPercent,
                        GstAmount = reservation.GstAmountInr,
                        CancellationPoliciesJson = bus.CancellationPoliciesJson,

                        AutoDiscountAmount =
    reservation.AutoDiscountAmountInr,

                        CouponDiscountAmount =
    reservation.CouponDiscountAmountInr,

                        // Per-passenger details
                        Passengers = passengers.Select(p => new BusPassengerSeatDto
                        {
                            FullName = p.FullName,
                            Gender = p.Gender,
                            SeatNumber = p.SeatNumber ?? string.Empty
                        }).ToList()
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Booking email failed for {BookingReference}", reservation.BookingReference);
                }
            }

            // ---------------- WHATSAPP ----------------
            var message = $@"
                Booking Confirmed ✅

                Ref: {reservation.BookingReference}
                Route: {bus.FromCity} → {bus.ToCity}
                Seats: {seatNumbers}
                Departure: {bus.DepartureTime}
                ";

            var (sent, msg) = await _whatsAppService.SendTextAsync(
                reservation.PassengerPhone,
                message
            );

            if (!sent)
                logger.LogWarning("WhatsApp booking failed: {Message}", msg);
        }
        private (decimal RefundAmount, decimal CancellationCharge) CalculateSrdvRefund(BusBooking bus, IReadOnlyList<BusReservationPassenger> cancelledPassengers, decimal netFareInr, int totalBookedSeats)
        {
            var cancelledSeats = cancelledPassengers.Count;
            var refundablePool = netFareInr;
            var proportionalPrice = totalBookedSeats > 0 ? (refundablePool / totalBookedSeats) * cancelledSeats : 0m;
            var cancelledBaseFare = cancelledPassengers.Sum(p => p.BaseFareInr);

            if (string.IsNullOrEmpty(bus.CancellationPoliciesJson))
            {
                // Fallback to no refund if we don't have policy
                return (0m, proportionalPrice);
            }

            try
            {
                var policies = System.Text.Json.JsonSerializer.Deserialize<List<SrdvCancellationPolicyDto>>(bus.CancellationPoliciesJson);
                if (policies == null || !policies.Any()) return (0m, proportionalPrice);

                var istNow = DateTime.UtcNow.Add(IndiaOffset);
                var istDeparture = bus.DepartureTime.Add(IndiaOffset);
                var hoursBeforeDeparture = (istDeparture - istNow).TotalHours;

                // Sort by TimeBeforeDept to find the right tier
                // Note: "-1" is usually the catch-all for "anytime before".
                var orderedPolicies = policies
                    .Select(p => new { Policy = p, Hours = double.TryParse(p.TimeBeforeDept, out var h) ? h : 0 })
                    .OrderBy(x => x.Hours < 0 ? double.MaxValue : x.Hours)
                    .ToList();

                decimal matchedCharge = 0m; // Default to 0% penalty for early cancellations
                string chargeType = "Percentage";

                foreach (var tier in orderedPolicies)
                {
                    if (tier.Hours > 0 && hoursBeforeDeparture <= tier.Hours)
                    {
                        matchedCharge = decimal.TryParse(tier.Policy.CancellationCharge, out var c) ? c : 0m;
                        chargeType = tier.Policy.CancellationChargeType ?? "Percentage";
                        break;
                    }
                    if (tier.Hours < 0) // The -1 catch all
                    {
                        matchedCharge = decimal.TryParse(tier.Policy.CancellationCharge, out var c) ? c : 0m;
                        chargeType = tier.Policy.CancellationChargeType ?? "Percentage";
                        break;
                    }
                }

                decimal cancellationCharge = 0m;
                if (chargeType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
                {
                    cancellationCharge = (cancelledBaseFare * matchedCharge) / 100m;
                }
                else
                {
                    // Fixed amount per seat
                    cancellationCharge = matchedCharge * cancelledSeats;
                }

                if (cancellationCharge > proportionalPrice) cancellationCharge = proportionalPrice;
                if (cancellationCharge < 0) cancellationCharge = 0;

                decimal refundAmount = proportionalPrice - cancellationCharge;
                if (refundAmount < 0) refundAmount = 0;
                
                return (decimal.Round(refundAmount, 2), decimal.Round(cancellationCharge, 2));
            }
            catch
            {
                // Fallback to no refund on parse error
                return (0m, proportionalPrice);
            }
        }
    }

}
