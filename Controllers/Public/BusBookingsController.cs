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
    IBusCouponContextBuilder couponContextBuilder,
    ITicketEmailService ticketEmailService,
    IWhatsAppService whatsAppService,
    ICurrentUserService currentUserService,
    ISrdvBusService srdvBusService,
    IMemoryCache cache,
    PickNBook.Api.Services.Interfaces.ICancellationRefundCalculator refundCalculator,
    PickNBook.Api.Services.Interfaces.ICashfreeService cashfreeService,
    BusCityCacheService busCityCacheService,
    ILogger<BusBookingsController> logger) : BaseApiController
    {
        //private const string UserIdHeaderName = "X-User-Id";
        private readonly IBusPromotionEngineService _promotionEngine = promotionEngine;
        private readonly IBusCouponContextBuilder _couponContextBuilder = couponContextBuilder;
        private readonly ISrdvBusService _srdvBusService = srdvBusService;
        private readonly IMemoryCache _cache = cache;
        private readonly BusCityCacheService _busCityCacheService = busCityCacheService;

        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);
        private static readonly string[] AllowedPassengerGenders = ["Male", "Female"];
        private readonly IWhatsAppService _whatsAppService = whatsAppService;
        private readonly ITicketEmailService _ticketEmailService = ticketEmailService;

        [HttpGet("user/available")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAvailableCoupons(
            [FromQuery] string? category = null,
            [FromQuery] string? traceId = null,
            [FromQuery] string? resultIndex = null,
            [FromQuery] string? seatCodes = null)
        {
            try
            {
                var today = DateOnly.FromDateTime(
                    DateTime.UtcNow.AddHours(5.5));

                var query = dbContext.BusCoupons
                    .Include(x => x.Conditions)
                    .AsNoTracking()
                    .Where(x =>
                        x.Status == "Active" &&
                        x.StartDate <= today &&
                        x.ExpiryDate >= today &&
                        (x.UseLimit == 0 || x.UsedCount < x.UseLimit));

                if (!string.IsNullOrWhiteSpace(category))
                {
                    query = query.Where(x => x.PromotionCategory == category);
                }

                var coupons = await query
                    .OrderBy(x => x.ExpiryDate)
                    .ToListAsync();

                BusCouponValidationContext? validationContext = null;
                if (!string.IsNullOrWhiteSpace(traceId) && !string.IsNullOrWhiteSpace(resultIndex))
                {
                    var seatsList = !string.IsNullOrWhiteSpace(seatCodes)
                        ? seatCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : new List<string>();

                    validationContext = await _couponContextBuilder.BuildContextAsync(traceId, resultIndex, seatsList);
                }

                var response = coupons.Select(x =>
                {
                    bool isEligible = true;
                    if (validationContext != null)
                    {
                        isEligible = _promotionEngine.ValidateCouponConditions(x.Conditions, validationContext);
                    }

                    return new
                    {
                        x.Id,
                        x.CouponCode,
                        x.CouponType,
                        x.Value,
                        x.MaxDiscountAmount,
                        x.MinBookingAmount,
                        x.MaxUsagePerUser,
                        x.ExpiryDate,
                        PromotionCategory = x.PromotionCategory,
                        Title = x.Title ?? x.CouponCode,
                        Description = x.Description ?? x.Remark,
                        x.IsAutoApply,
                        x.IsExclusive,
                        IsEligible = isEligible
                    };
                }).ToList();

                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            if (request.FromCityCode <= 0)
            {
                return BadRequest("FromCityCode must be greater than 0.");
            }

            if (request.ToCityCode <= 0)
            {
                return BadRequest("ToCityCode must be greater than 0.");
            }

            if (request.FromCityCode == request.ToCityCode)
            {
                return BadRequest("FromCityCode and ToCityCode cannot be the same.");
            }

            if (!_busCityCacheService.IsValidCity(request.FromCityCode))
            {
                return BadRequest($"FromCityCode '{request.FromCityCode}' does not exist in the active bus city directory.");
            }

            if (!_busCityCacheService.IsValidCity(request.ToCityCode))
            {
                return BadRequest($"ToCityCode '{request.ToCityCode}' does not exist in the active bus city directory.");
            }

            if (string.IsNullOrWhiteSpace(request.DepartDate) || 
                !DateOnly.TryParseExact(request.DepartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var journeyDate))
            {
                return BadRequest("DepartDate must be a valid date in YYYY-MM-DD format.");
            }

            var todayIst = DateOnly.FromDateTime(DateTime.UtcNow.Add(IndiaOffset));
            if (journeyDate < todayIst)
            {
                return BadRequest("DepartDate cannot be in the past.");
            }

            try
            {
                var rawJson = await _srdvBusService.SearchBusesProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);
                
                if (jsonNode?["Result"]?.AsArray() is var resultNode && resultNode != null)
                {
                    var (seaterMarkup, sleeperMarkup) = await GetBothMarkupsAsync();

                    var journeyDateStr = request.DepartDate; // Format: dd/mm/yyyy or yyyy-MM-dd
                    DateOnly.TryParseExact(journeyDateStr, new[] { "dd/MM/yyyy", "yyyy-MM-dd" }, null, System.Globalization.DateTimeStyles.None, out journeyDate);

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

                        var searchTraceId = jsonNode["TraceId"]?.ToString() ?? string.Empty;
                        var resIdx = busNode["ResultIndex"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(searchTraceId) && !string.IsNullOrEmpty(resIdx))
                        {
                            var srdvIdx = long.TryParse(busNode["SrdvIndex"]?.ToString(), out var si) ? si : 0L;
                            var bpDpSeatLayout = busNode["BpDpSeatLayout"]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                            var busCtx = new BusSearchItemContext
                            {
                                TraceId = searchTraceId,
                                ResultIndex = resIdx,
                                SrdvIndex = srdvIdx,
                                OperatorName = busNode["TravelsName"]?.ToString() ?? string.Empty,
                                BusType = busNode["BusType"]?.ToString() ?? string.Empty,
                                FromCity = request.FromCityCode.ToString(),
                                ToCity = request.ToCityCode.ToString(),
                                DepartureTime = busNode["DepartureTime"]?.ToString() ?? string.Empty,
                                ArrivalTime = busNode["ArrivalTime"]?.ToString() ?? string.Empty,
                                DepartDate = request.DepartDate,
                                BpDpSeatLayout = bpDpSeatLayout
                            };
                            _cache.Set($"bus_ctx_{searchTraceId}_{resIdx}", busCtx, TimeSpan.FromHours(1));

                            var compositeResIdx = SrdvBusService.BuildCompositeResultIndex(resIdx, srdvIdx.ToString());
                            if (!string.Equals(compositeResIdx, resIdx, StringComparison.OrdinalIgnoreCase))
                            {
                                _cache.Set($"bus_ctx_{searchTraceId}_{compositeResIdx}", busCtx, TimeSpan.FromHours(1));
                            }
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
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.TraceId) || !long.TryParse(request.TraceId, out var traceIdNum) || traceIdNum <= 0)
            {
                return BadRequest("TraceId must be present and greater than 0.");
            }

            if (string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest("ResultIndex is required.");
            }

            var compositeResultIndex = SrdvBusService.BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex);
            bool foundInCache = _cache.TryGetValue($"bus_ctx_{request.TraceId}_{request.ResultIndex}", out BusSearchItemContext? busCtx)
                || _cache.TryGetValue($"bus_ctx_{request.TraceId}_{compositeResultIndex}", out busCtx);

            if (foundInCache && busCtx != null)
            {
                if (string.IsNullOrWhiteSpace(request.SrdvIndex) && busCtx.SrdvIndex > 0)
                {
                    request.SrdvIndex = busCtx.SrdvIndex.ToString();
                }
            }

            bool isBpDp = (busCtx != null && busCtx.BpDpSeatLayout) || (request.BpDpSeatLayout == true);
            if (isBpDp)
            {
                if (string.IsNullOrWhiteSpace(request.BoardingPointId) || string.IsNullOrWhiteSpace(request.DroppingPointId))
                {
                    return BadRequest("BoardingPointId and DroppingPointId are required for BP-DP seat layout.");
                }
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

                    var seatLayoutMap = new Dictionary<string, BusSeatLayoutItemContext>(StringComparer.OrdinalIgnoreCase);
                    foreach (var seatNode in resultNodes)
                    {
                        if (seatNode == null) continue;
                        var sn = seatNode["SeatName"]?.ToString();
                        var st = seatNode["SeatType"]?.ToString() ?? "";
                        decimal.TryParse(seatNode["Price"]?["BaseFare"]?.ToString(), out decimal bf);
                        decimal.TryParse(seatNode["SeatFare"]?.ToString(), out decimal sf);
                        decimal.TryParse(seatNode["Price"]?["PublishedFare"]?.ToString(), out decimal pf);
                        decimal.TryParse(seatNode["Price"]?["GSTAmount"]?.ToString() ?? seatNode["Price"]?["Tax"]?.ToString() ?? seatNode["Price"]?["GstAmount"]?.ToString(), out decimal gst);

                        var statusStr = seatNode["SeatStatus"]?.ToString()?.Trim() ?? "";
                        bool isAvailable = true;
                        if (bool.TryParse(statusStr, out var bStatus)) isAvailable = bStatus;
                        else if (statusStr.Equals("Available", StringComparison.OrdinalIgnoreCase) || statusStr == "1") isAvailable = true;
                        else if (statusStr.Equals("Booked", StringComparison.OrdinalIgnoreCase) || statusStr == "0" || statusStr.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) || statusStr.Equals("false", StringComparison.OrdinalIgnoreCase)) isAvailable = false;

                        var isLadies = seatNode["IsLadiesSeat"]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                            || seatNode["LadiesSeat"]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                            || seatNode["Gender"]?.ToString()?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true
                            || seatNode["ReservedFor"]?.ToString()?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true;

                        var isMales = seatNode["IsMalesSeat"]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                            || seatNode["Gender"]?.ToString()?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true
                            || seatNode["ReservedFor"]?.ToString()?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true;

                        if (!string.IsNullOrWhiteSpace(sn))
                        {
                            seatLayoutMap[sn] = new BusSeatLayoutItemContext
                            {
                                SeatName = sn,
                                SeatType = st,
                                BaseFare = bf,
                                SeatFare = sf,
                                PublishedFare = pf,
                                GstAmount = gst,
                                IsAvailable = isAvailable,
                                SeatStatus = statusStr,
                                IsLadiesSeat = isLadies,
                                IsMalesSeat = isMales
                            };
                        }
                    }
                    _cache.Set($"bus_seats_{request.TraceId}_{request.ResultIndex}", seatLayoutMap, TimeSpan.FromHours(1));
                    if (!string.Equals(compositeResultIndex, request.ResultIndex, StringComparison.OrdinalIgnoreCase))
                    {
                        _cache.Set($"bus_seats_{request.TraceId}_{compositeResultIndex}", seatLayoutMap, TimeSpan.FromHours(1));
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
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.TraceId) || !long.TryParse(request.TraceId, out var traceIdNum) || traceIdNum <= 0)
            {
                return BadRequest("TraceId must be present, numeric, and greater than 0.");
            }

            if (string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest("ResultIndex is required.");
            }

            var compositeResultIndex = SrdvBusService.BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex);
            bool foundInCache = _cache.TryGetValue($"bus_ctx_{request.TraceId}_{request.ResultIndex}", out BusSearchItemContext? busCtx)
                || _cache.TryGetValue($"bus_ctx_{request.TraceId}_{compositeResultIndex}", out busCtx);

            if (!foundInCache || busCtx == null)
            {
                return BadRequest(new { message = "Invalid or expired search workflow session. TraceId and ResultIndex must belong to an active search within 1 hour." });
            }

            if (string.IsNullOrWhiteSpace(request.SrdvIndex) && busCtx.SrdvIndex > 0)
            {
                request.SrdvIndex = busCtx.SrdvIndex.ToString();
            }

            try
            {
                var rawJson = await _srdvBusService.GetBoardingPointDetailsProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);

                if (jsonNode is System.Text.Json.Nodes.JsonObject rootObj)
                {
                    var bpWorkflow = new BusBoardingPointsWorkflowContext();
                    void ExtractPoints(System.Text.Json.Nodes.JsonNode? arrNode, HashSet<string> targetSet)
                    {
                        if (arrNode is System.Text.Json.Nodes.JsonArray arr)
                        {
                            foreach (var item in arr)
                            {
                                if (item == null) continue;
                                var id = item["CityPointIndex"]?.ToString()
                                      ?? item["CityPointLocationId"]?.ToString()
                                      ?? item["PointId"]?.ToString()
                                      ?? item["Id"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(id))
                                {
                                    targetSet.Add(id.Trim());
                                }
                            }
                        }
                    }

                    ExtractPoints(rootObj["BoardingPointsDetails"] ?? rootObj["BoardingPoints"], bpWorkflow.BoardingPointIds);
                    ExtractPoints(rootObj["DroppingPointsDetails"] ?? rootObj["DroppingPoints"], bpWorkflow.DroppingPointIds);

                    if (bpWorkflow.BoardingPointIds.Count > 0 || bpWorkflow.DroppingPointIds.Count > 0)
                    {
                        _cache.Set($"bus_bp_{request.TraceId}_{request.ResultIndex}", bpWorkflow, TimeSpan.FromHours(1));
                        if (!string.Equals(compositeResultIndex, request.ResultIndex, StringComparison.OrdinalIgnoreCase))
                        {
                            _cache.Set($"bus_bp_{request.TraceId}_{compositeResultIndex}", bpWorkflow, TimeSpan.FromHours(1));
                        }
                    }
                }
                
                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch boarding points from SRDV proxy.");
                return StatusCode(500, new { message = "Error fetching boarding points from provider." });
            }
        }

        [HttpPost("v9/Block")]
        [HttpPost("block")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> BlockBusProxy([FromBody] SrdvBusBookingRequestDto request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.TraceId) || !long.TryParse(request.TraceId, out var traceIdNum) || traceIdNum <= 0)
            {
                return BadRequest("TraceId must be present, numeric, and greater than 0.");
            }

            if (string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest("ResultIndex is required.");
            }

            var compositeResultIndex = SrdvBusService.BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex.ToString());
            bool foundSearchCtx = _cache.TryGetValue($"bus_ctx_{request.TraceId}_{request.ResultIndex}", out BusSearchItemContext? busCtx)
                || _cache.TryGetValue($"bus_ctx_{request.TraceId}_{compositeResultIndex}", out busCtx);

            if (!foundSearchCtx || busCtx == null)
            {
                return BadRequest(new { message = "Invalid or expired search workflow session. TraceId and ResultIndex must belong to an active search within 1 hour." });
            }

            if (request.SrdvIndex <= 0 && busCtx.SrdvIndex > 0)
            {
                request.SrdvIndex = busCtx.SrdvIndex;
            }

            if (string.IsNullOrWhiteSpace(request.BoardingPointId))
            {
                return BadRequest(new { message = "BoardingPointId is required." });
            }
            if (string.IsNullOrWhiteSpace(request.DroppingPointId))
            {
                return BadRequest(new { message = "DroppingPointId is required." });
            }

            // Validate against retained Boarding Point workflow state if available
            bool foundBpState = _cache.TryGetValue($"bus_bp_{request.TraceId}_{request.ResultIndex}", out BusBoardingPointsWorkflowContext? bpCtx)
                || _cache.TryGetValue($"bus_bp_{request.TraceId}_{compositeResultIndex}", out bpCtx);

            if (foundBpState && bpCtx != null)
            {
                if (bpCtx.BoardingPointIds.Count > 0 && !bpCtx.BoardingPointIds.Contains(request.BoardingPointId.Trim()))
                {
                    return BadRequest(new { message = $"BoardingPointId '{request.BoardingPointId}' does not belong to the valid boarding points for this bus." });
                }
                if (bpCtx.DroppingPointIds.Count > 0 && !bpCtx.DroppingPointIds.Contains(request.DroppingPointId.Trim()))
                {
                    return BadRequest(new { message = $"DroppingPointId '{request.DroppingPointId}' does not belong to the valid dropping points for this bus." });
                }
            }

            if (request.Passengers == null || request.Passengers.Count < 1 || request.Passengers.Count > 10)
            {
                return BadRequest(new { message = "Passengers count must be between 1 and 10." });
            }

            int leadPassengerCount = request.Passengers.Count(p => p.LeadPassenger == true);
            if (leadPassengerCount > 1)
            {
                return BadRequest(new { message = "Exactly one passenger must be designated as the lead passenger." });
            }
            else if (leadPassengerCount == 0)
            {
                request.Passengers[0].LeadPassenger = true;
            }

            var seatNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var emailRegex = new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

            for (int i = 0; i < request.Passengers.Count; i++)
            {
                var p = request.Passengers[i];
                if (string.IsNullOrWhiteSpace(p.Title) || p.Title.Trim().Length > 20)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: Title is required and must be max 20 characters." });
                }
                if (string.IsNullOrWhiteSpace(p.FirstName) || p.FirstName.Trim().Length > 100)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: FirstName is required and must be max 100 characters." });
                }
                if (string.IsNullOrWhiteSpace(p.LastName) || p.LastName.Trim().Length > 100)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: LastName is required and must be max 100 characters." });
                }

                var gStr = p.Gender.ToString().Trim();
                if (gStr != "1" && gStr != "2" && !gStr.Equals("Male", StringComparison.OrdinalIgnoreCase) && !gStr.Equals("Female", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: Gender must be '1' (Male) or '2' (Female)." });
                }
                if (gStr.Equals("Male", StringComparison.OrdinalIgnoreCase)) p.Gender = 1;
                else if (gStr.Equals("Female", StringComparison.OrdinalIgnoreCase)) p.Gender = 2;

                if (p.Age < 1 || p.Age > 120)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: Age must be between 1 and 120." });
                }

                if (string.IsNullOrWhiteSpace(p.Email) || p.Email.Length > 254 || !emailRegex.IsMatch(p.Email.Trim()))
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: A valid email (max 254 characters) is required." });
                }

                var cleanPhone = p.ContactNo?.Trim() ?? "";
                if (cleanPhone.Length < 5 || cleanPhone.Length > 20)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: PhoneNo must be between 5 and 20 characters." });
                }

                if (!string.IsNullOrWhiteSpace(p.IdNumber) && p.IdNumber.Trim().Length > 100)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: IdNumber must be max 100 characters." });
                }
                if (!string.IsNullOrWhiteSpace(p.IdType) && p.IdType.Trim().Length > 50)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: IdType must be max 50 characters." });
                }

                if (string.IsNullOrWhiteSpace(p.Address))
                {
                    p.Address = "India";
                }
                else if (p.Address.Trim().Length > 500)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: Address must be max 500 characters." });
                }

                if (string.IsNullOrWhiteSpace(p.SeatName) || p.SeatName.Trim().Length > 100)
                {
                    return BadRequest(new { message = $"Passenger {i + 1}: SeatName is required and must be max 100 characters." });
                }

                var trimmedSeat = p.SeatName.Trim();
                if (!seatNamesSet.Add(trimmedSeat))
                {
                    return BadRequest(new { message = $"Duplicate seat '{trimmedSeat}' detected in passenger list. Duplicate seats are not allowed." });
                }

                // GST validation
                if (!string.IsNullOrWhiteSpace(p.GSTNumber))
                {
                    if (string.IsNullOrWhiteSpace(p.GSTCompanyName))
                    {
                        return BadRequest(new { message = $"Passenger {i + 1}: GSTCompanyName is required when GSTNumber is provided." });
                    }
                    if (string.IsNullOrWhiteSpace(p.GSTCompanyAddress))
                    {
                        return BadRequest(new { message = $"Passenger {i + 1}: GSTCompanyAddress is required when GSTNumber is provided." });
                    }
                    if (!string.IsNullOrWhiteSpace(p.GSTCompanyEmail) && !emailRegex.IsMatch(p.GSTCompanyEmail.Trim()))
                    {
                        return BadRequest(new { message = $"Passenger {i + 1}: GSTCompanyEmail must be a valid email." });
                    }
                }
            }

            // Validate Seats against valid SeatLayout
            bool foundSeats = _cache.TryGetValue($"bus_seats_{request.TraceId}_{request.ResultIndex}", out Dictionary<string, BusSeatLayoutItemContext>? layoutMap)
                || _cache.TryGetValue($"bus_seats_{request.TraceId}_{compositeResultIndex}", out layoutMap);

            if (foundSeats && layoutMap != null)
            {
                foreach (var p in request.Passengers)
                {
                    var seatName = p.SeatName.Trim();
                    if (!layoutMap.TryGetValue(seatName, out var layoutSeat))
                    {
                        return BadRequest(new { message = $"Seat '{seatName}' does not exist in the valid seat layout for this bus." });
                    }
                    if (!layoutSeat.IsAvailable)
                    {
                        return BadRequest(new { message = $"Seat '{seatName}' is not available for booking." });
                    }

                    // RedBus forcedSeats rule check
                    if (layoutSeat.IsLadiesSeat && p.Gender == 1)
                    {
                        return Ok(new
                        {
                            Error = new
                            {
                                ErrorCode = 7040,
                                ErrorMessage = "Under the RedBus forcedSeats rule, available reserved seats for each gender must be selected first."
                            }
                        });
                    }
                }
            }

            // Idempotency check: identical payload returns retained response, different payload returns 7019
            var payloadSignature = $"{request.BoardingPointId?.Trim()}_{request.DroppingPointId?.Trim()}_" +
                string.Join(";", request.Passengers.OrderBy(p => p.SeatName).Select(p => $"{p.SeatName}:{p.Gender}:{p.FirstName}_{p.LastName}"));

            var idempotencyKey = $"bus_block_idem_{request.TraceId}_{compositeResultIndex}";
            if (_cache.TryGetValue(idempotencyKey, out (string SavedSignature, string SavedJson) cachedBlock))
            {
                if (string.Equals(cachedBlock.SavedSignature, payloadSignature, StringComparison.Ordinal))
                {
                    var parsedRetained = System.Text.Json.Nodes.JsonNode.Parse(cachedBlock.SavedJson);
                    return Ok(parsedRetained);
                }
                else
                {
                    return Ok(new
                    {
                        Error = new
                        {
                            ErrorCode = 7019,
                            ErrorMessage = "Payload mismatch on block retry."
                        }
                    });
                }
            }

            try
            {
                var rawJson = await _srdvBusService.BlockBusProxyAsync(request);
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(rawJson);

                if (jsonNode is System.Text.Json.Nodes.JsonObject jsonObj)
                {
                    var errObj = jsonObj["Error"] as System.Text.Json.Nodes.JsonObject;
                    int errCode = -1;
                    if (errObj != null && int.TryParse(errObj["ErrorCode"]?.ToString(), out var ec))
                    {
                        errCode = ec;
                    }

                    var resultObj = jsonObj["Result"] as System.Text.Json.Nodes.JsonObject;
                    var blockKeyStr = jsonObj["BlockKey"]?.ToString() ?? resultObj?["BlockKey"]?.ToString();

                    if (errCode == 0)
                    {
                        _cache.Set(idempotencyKey, (payloadSignature, rawJson), TimeSpan.FromHours(1));

                        if (!string.IsNullOrWhiteSpace(blockKeyStr))
                        {
                            _cache.Set($"bus_blockkey_{request.TraceId}_{request.ResultIndex}", blockKeyStr.Trim(), TimeSpan.FromHours(1));
                            if (!string.Equals(compositeResultIndex, request.ResultIndex, StringComparison.OrdinalIgnoreCase))
                            {
                                _cache.Set($"bus_blockkey_{request.TraceId}_{compositeResultIndex}", blockKeyStr.Trim(), TimeSpan.FromHours(1));
                            }
                        }

                        if (request.Passengers != null && request.Passengers.Count > 0)
                        {
                            _cache.Set($"bus_block_passengers_{request.TraceId}_{request.ResultIndex}", request.Passengers, TimeSpan.FromHours(1));
                            if (!string.Equals(compositeResultIndex, request.ResultIndex, StringComparison.OrdinalIgnoreCase))
                            {
                                _cache.Set($"bus_block_passengers_{request.TraceId}_{compositeResultIndex}", request.Passengers, TimeSpan.FromHours(1));
                            }
                        }
                    }

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
                                var dbRecord = blockedSeatsInDb
                                    .Where(x => string.Equals(
                                        x.SeatName,
                                        seatNameStr,
                                        StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(x => x.Id)
                                    .FirstOrDefault();

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




                var seatCodes = request.Seats
                    .Where(p => !string.IsNullOrWhiteSpace(p.SeatCode))
                    .Select(p => p.SeatCode.Trim())
                    .ToList();

                if (!seatCodes.Any())
                {
                    return BadRequest(new { message = "At least one seat is required for pricing preview." });
                }

                // Mandatory upfront layout check for SeatType
                Dictionary<string, BusSeatLayoutItemContext>? layoutMap = null;
                if (!string.IsNullOrEmpty(request.TraceId) && !string.IsNullOrEmpty(request.ResultIndex))
                {
                    _cache.TryGetValue($"bus_seats_{request.TraceId}_{request.ResultIndex}", out layoutMap);
                }

                var missingLayoutSeats = seatCodes
                    .Where(seat => layoutMap == null || 
                                   !layoutMap.TryGetValue(seat, out var layoutSeat) || 
                                   string.IsNullOrWhiteSpace(layoutSeat.SeatType))
                    .ToList();

                if (missingLayoutSeats.Any())
                {
                    return BadRequest(new { 
                        message = $"Authoritative seat layout information is unavailable for seat(s): {string.Join(", ", missingLayoutSeats)}. Please refresh the seat layout and try again." 
                    });
                }

                // ========================================
                // CENTRALIZED PRICING ENGINE
                // ========================================
                var traceId = request.TraceId ?? string.Empty;
                var blockedSeats = await dbContext.BusBlockedSeatPrices
                    .Where(x => x.TraceId == traceId)
                    .ToListAsync();

                var seatPreviews = new List<PickNBook.Api.Models.DTOs.SeatPreviewDto>();
                foreach (var p in request.Seats.Where(s => !string.IsNullOrWhiteSpace(s.SeatCode)))
                {
                    var seatCode = p.SeatCode.Trim();
                    var blockedSeat = blockedSeats
                        .OrderByDescending(b => b.Id)
                        .FirstOrDefault(b => b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));

                    var layoutSeat = layoutMap![seatCode];

                    decimal baseFare = 0m;
                    decimal gstAmount = 0m;

                    if (blockedSeat != null && blockedSeat.BaseFare > 0)
                    {
                        baseFare = blockedSeat.BaseFare;
                        gstAmount = blockedSeat.GstAmount;
                    }
                    else
                    {
                        baseFare = layoutSeat.BaseFare > 0 ? layoutSeat.BaseFare : p.BaseFare;
                        gstAmount = layoutSeat.GstAmount > 0 
                            ? layoutSeat.GstAmount 
                            : (p.ExternalGst > 0 ? p.ExternalGst : (layoutSeat.PublishedFare > layoutSeat.BaseFare ? layoutSeat.PublishedFare - layoutSeat.BaseFare : 0m));
                    }

                    if (baseFare <= 0)
                    {
                        return BadRequest(new { 
                            message = $"Authoritative seat pricing is unavailable for seat '{seatCode}'. Please refresh the seat layout." 
                        });
                    }

                    seatPreviews.Add(new PickNBook.Api.Models.DTOs.SeatPreviewDto 
                    { 
                        SeatCode = seatCode, 
                        BaseFare = baseFare, 
                        SeatType = layoutSeat.SeatType, // 100% authoritative from layout
                        ExternalGst = gstAmount 
                    });
                }

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

                seatCodes = seatPreviews.Select(s => s.SeatCode).ToList();
                var validationContext = await _couponContextBuilder.BuildContextAsync(
                    request.TraceId,
                    request.ResultIndex,
                    seatCodes,
                    dummyBus,
                    seatPreviews);

                var pricing = await _promotionEngine.CalculateAsync(
                    dummyBus,
                    seatPreviews,
                    request.CouponCode,
                    request.PromotionId,
                    parsedUserId,
                    request.SelectedFeaturedOfferId,
                    validationContext);

                return Ok(pricing);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private sealed class BusBookingException(int errorCode, string message) : Exception(message)
        {
            public int ErrorCode { get; } = errorCode;
        }

        private static (bool IsConfirmed, decimal CancellationCharge, decimal RefundAmount) TryReconcileCancellationFromDetails(
            SrdvBusBookingDetailsResponseDto? details,
            IEnumerable<string> targetSeatNumbers,
            long? targetCancelId = null)
        {
            if (details == null || !details.Success || details.Result == null)
            {
                return (false, 0m, 0m);
            }

            var seatsSet = new HashSet<string>(targetSeatNumbers.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            if (seatsSet.Count == 0 && (!targetCancelId.HasValue || targetCancelId.Value <= 0))
            {
                return (false, 0m, 0m);
            }

            if (details.Result.Cancellations != null && details.Result.Cancellations.Any())
            {
                // Prefer exact CancelId correlation if available
                SrdvBusBookingDetailsCancellationDto? matchingCancellation = null;
                if (targetCancelId.HasValue && targetCancelId.Value > 0)
                {
                    matchingCancellation = details.Result.Cancellations
                        .FirstOrDefault(c => c.CancelId == targetCancelId.Value);
                }

                // Fallback to strict seat set matching on recent records
                if (matchingCancellation == null && seatsSet.Count > 0)
                {
                    matchingCancellation = details.Result.Cancellations
                        .OrderByDescending(c => c.CompletedAt ?? DateTime.MinValue)
                        .FirstOrDefault(c =>
                        {
                            var isCancelled = string.Equals(c.Status, "Success", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(c.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(c.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                                || !string.IsNullOrWhiteSpace(c.SupplierCancelId);

                            if (!isCancelled || c.SeatName == null || c.SeatName.Count == 0) return false;

                            var cSeats = new HashSet<string>(c.SeatName.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
                            return cSeats.SetEquals(seatsSet);
                        });
                }

                if (matchingCancellation != null)
                {
                    // Strict correlation to this specific cancellation operation; never sum historical records
                    return (true, matchingCancellation.CancellationCharge, matchingCancellation.RefundAmount);
                }
            }

            // Fallback: Check if passenger records confirm the cancellation status
            if (seatsSet.Count > 0 && details.Result.Passengers != null && details.Result.Passengers.Any())
            {
                var cancelledPax = details.Result.Passengers
                    .Where(p => !string.IsNullOrWhiteSpace(p.SeatName) && seatsSet.Contains(p.SeatName) &&
                               (string.Equals(p.CancelStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.CancelStatus, "Success", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (cancelledPax.Count == seatsSet.Count)
                {
                    // Confirmed cancelled on provider, but without a dedicated correlated cancellation financial record
                    return (true, 0m, 0m);
                }
            }

            return (false, 0m, 0m);
        }

        [HttpPost("book")]
        public async Task<IActionResult> BookBus([FromBody] CreateBusBookingRequestDto request)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to continue booking.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.TraceId) || !long.TryParse(request.TraceId, out var traceIdNum) || traceIdNum <= 0)
            {
                return BadRequest(new { message = "TraceId must be present, numeric, and greater than 0." });
            }

            if (string.IsNullOrWhiteSpace(request.ResultIndex))
            {
                return BadRequest(new { message = "ResultIndex is required." });
            }

            var compositeResultIndex = SrdvBusService.BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex.ToString());
            bool foundSearchCtx = _cache.TryGetValue($"bus_ctx_{request.TraceId}_{request.ResultIndex}", out BusSearchItemContext? busCtx)
                || _cache.TryGetValue($"bus_ctx_{request.TraceId}_{compositeResultIndex}", out busCtx);

            if (!foundSearchCtx || busCtx == null)
            {
                return BadRequest(new { message = "Invalid or expired search workflow session. TraceId and ResultIndex must belong to an active search within 1 hour." });
            }

            if (request.SrdvIndex <= 0 && busCtx.SrdvIndex > 0)
            {
                request.SrdvIndex = busCtx.SrdvIndex;
            }

            var passengerValidationError = ValidateAndNormalizePassengers(request.Passengers, out var normalizedPassengers);
            if (passengerValidationError is not null)
                return BadRequest(passengerValidationError);

            if (string.IsNullOrWhiteSpace(request.PassengerPhone))
                return BadRequest("PassengerPhone is required for contact.");

            var leadPassenger = normalizedPassengers!.FirstOrDefault(p => p.LeadPassenger == true);
            var contactName = string.IsNullOrWhiteSpace(request.PassengerName)
                ? (leadPassenger?.FullName ?? normalizedPassengers![0].FullName)
                : request.PassengerName.Trim();

            if (string.IsNullOrWhiteSpace(contactName))
                return BadRequest("PassengerName is required for contact.");

            // Verify frozen block state exists
            bool hasBlockKey = !string.IsNullOrWhiteSpace(request.BlockKey)
                || _cache.TryGetValue($"bus_blockkey_{request.TraceId}_{request.ResultIndex}", out _)
                || _cache.TryGetValue($"bus_blockkey_{request.TraceId}_{compositeResultIndex}", out _);

            if (!hasBlockKey)
            {
                return BadRequest(new { message = "Seats have not been blocked or the block session has expired. Please block the seats before booking." });
            }

            // =========================================================================
            // HARD DB-BACKED BOOK IDEMPOTENCY & RETRY GUARD
            // =========================================================================
            var existingReservation = await dbContext.BusReservations
                .Include(r => r.BusBooking)
                .FirstOrDefaultAsync(r => r.UserId == userId && r.BusBooking != null && r.BusBooking.TraceId == request.TraceId);

            if (existingReservation != null)
            {
                // A) EXISTING SUCCESS (or Booked)
                if (BusBookingStatus.IsConfirmed(existingReservation.Status))
                {
                    // Repair financial state if needed
                    if (string.Equals(existingReservation.FinancialStatus, "DEBIT_FAILED_MANUAL_RECOVERY", StringComparison.OrdinalIgnoreCase)
                        && User?.IsInRole(AuthRoles.Agent) == true
                        && string.Equals(request.PaymentMethod, "Agent Wallet", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var walletService = HttpContext.RequestServices.GetRequiredService<IAgentWalletService>();
                            await walletService.DebitWalletForBookingAsync(
                                int.Parse(userId!),
                                existingReservation.TotalPriceInr,
                                existingReservation.BookingReference,
                                "Bus",
                                $"Bus Booking - {existingReservation.BusBooking?.FromCity} to {existingReservation.BusBooking?.ToCity} ({existingReservation.BusBooking?.OperatorName}) - Ref: {existingReservation.BookingReference}"
                            );
                            existingReservation.FinancialStatus = "DEDUCTED";
                            await dbContext.SaveChangesAsync();
                        }
                        catch (Exception repairEx)
                        {
                            logger.LogError(repairEx, "Failed to repair wallet debit on Book retry for {BookingRef}", existingReservation.BookingReference);
                        }
                    }

                    var existingPax = await dbContext.BusReservationPassengers
                        .AsNoTracking()
                        .Where(p => p.BusReservationId == existingReservation.Id)
                        .OrderBy(p => p.Id)
                        .ToListAsync();

                    return Ok(MapBusReservation(existingReservation, existingReservation.BusBooking!, existingPax));
                }

                // B) MANUAL_CHECK_REQUIRED: Reconcile via BookingDetails; DO NOT call BookBusAsync again!
                if (string.Equals(existingReservation.Status, BusBookingStatus.ManualCheckRequired, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Existing booking in MANUAL_CHECK_REQUIRED found for TraceId {TraceId}. Attempting reconciliation via BookingDetails.", request.TraceId);
                    SrdvBusBookingDetailsResponseDto? details = null;
                    try
                    {
                        details = await _srdvBusService.GetBookingDetailsAsync(request.TraceId);
                    }
                    catch (Exception detailsEx)
                    {
                        logger.LogWarning(detailsEx, "BookingDetails reconciliation failed during Book retry for TraceId {TraceId}", request.TraceId);
                    }

                    if (details != null && details.Success && details.Result != null)
                    {
                        var status = details.Result.BookingStatus?.Trim();
                        bool isConfirmed = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(status, "Confirmed", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(status, "Booked", StringComparison.OrdinalIgnoreCase);

                        if (isConfirmed)
                        {
                            existingReservation.Status = BusBookingStatus.Success;
                            existingReservation.SrdvTicketNo = details.Result.TicketNo;
                            existingReservation.Pnr = details.Result.TravelOperatorPNR ?? details.Result.TicketNo ?? existingReservation.Pnr;
                            existingReservation.SrdvBookingId = details.Result.BookingId?.ToString();
                            existingReservation.SrdvBookingResponseJson = details.ResponseJson;
                            await dbContext.SaveChangesAsync();

                            if (string.Equals(existingReservation.FinancialStatus, "DEDUCT_PENDING", StringComparison.OrdinalIgnoreCase))
                            {
                                if (User?.IsInRole(AuthRoles.Agent) == true && string.Equals(request.PaymentMethod, "Agent Wallet", StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        var walletService = HttpContext.RequestServices.GetRequiredService<IAgentWalletService>();
                                        await walletService.DebitWalletForBookingAsync(
                                            int.Parse(userId!),
                                            existingReservation.TotalPriceInr,
                                            existingReservation.BookingReference,
                                            "Bus",
                                            $"Bus Booking - {existingReservation.BusBooking?.FromCity} to {existingReservation.BusBooking?.ToCity} ({existingReservation.BusBooking?.OperatorName}) - Ref: {existingReservation.BookingReference}"
                                        );
                                        existingReservation.FinancialStatus = "DEDUCTED";
                                    }
                                    catch (Exception wEx)
                                    {
                                        logger.LogError(wEx, "Wallet debit failed after reconciliation for {BookingRef}", existingReservation.BookingReference);
                                        existingReservation.FinancialStatus = "DEBIT_FAILED_MANUAL_RECOVERY";
                                    }
                                    await dbContext.SaveChangesAsync();
                                }
                                else
                                {
                                    existingReservation.FinancialStatus = "DEDUCTED";
                                    await dbContext.SaveChangesAsync();
                                }
                            }

                            var existingPax = await dbContext.BusReservationPassengers
                                .AsNoTracking()
                                .Where(p => p.BusReservationId == existingReservation.Id)
                                .OrderBy(p => p.Id)
                                .ToListAsync();

                            return Ok(MapBusReservation(existingReservation, existingReservation.BusBooking!, existingPax));
                        }
                    }

                    return BadRequest(new
                    {
                        message = "MANUAL_CHECK_REQUIRED: Booking outcome is ambiguous and awaiting reconciliation.",
                        Error = new
                        {
                            ErrorCode = 7033,
                            ErrorMessage = "MANUAL_CHECK_REQUIRED: Booking outcome is ambiguous and awaiting reconciliation."
                        }
                    });
                }

                // C) BOOKING_IN_PROGRESS
                if (string.Equals(existingReservation.Status, BusBookingStatus.BookingInProgress, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        message = "Concurrent booking operation already in progress for this workflow.",
                        Error = new
                        {
                            ErrorCode = 7031,
                            ErrorMessage = "Concurrent booking operation already in progress for this workflow."
                        }
                    });
                }

                // D) FAILED
                if (string.Equals(existingReservation.Status, BusBookingStatus.Failed, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        message = $"SRDV Booking Failed: {existingReservation.CancellationReason ?? "Booking previously rejected"}",
                        Error = new
                        {
                            ErrorCode = 7032,
                            ErrorMessage = $"SRDV Booking Failed: {existingReservation.CancellationReason ?? "Booking previously rejected"}"
                        }
                    });
                }
            }

            // Idempotency: return stored completed booking response if already completed
            var idempotencyKey = $"bus_book_completed_{request.TraceId}_{compositeResultIndex}";
            if ((_cache.TryGetValue($"bus_book_completed_{request.TraceId}_{request.ResultIndex}", out string? cachedCompletedJson)
                 || _cache.TryGetValue(idempotencyKey, out cachedCompletedJson))
                && !string.IsNullOrWhiteSpace(cachedCompletedJson))
            {
                var cachedNode = System.Text.Json.Nodes.JsonNode.Parse(cachedCompletedJson);
                return Ok(cachedNode);
            }

            // Concurrency lock per TraceId
            var lockKey = $"bus_book_lock_{request.TraceId}";
            if (_cache.TryGetValue(lockKey, out _))
            {
                return BadRequest(new
                {
                    message = "Concurrent booking operation already in progress for this workflow.",
                    Error = new
                    {
                        ErrorCode = 7031,
                        ErrorMessage = "Concurrent booking operation already in progress for this workflow."
                    }
                });
            }

            _cache.Set(lockKey, true, TimeSpan.FromSeconds(60));

            try
            {
                var seatsRequired = normalizedPassengers!.Count;
                var strategy = dbContext.Database.CreateExecutionStrategy();

                // ========================================
                // PHASE 1: LOCAL TRANSACTIONAL PERSISTENCE
                // ========================================
                var (bus, reservation, passengers, pricing) = await strategy.ExecuteAsync(async () =>
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

                        // Mandatory upfront layout check for SeatType
                        Dictionary<string, BusSeatLayoutItemContext>? layoutMap = null;
                        if (!string.IsNullOrEmpty(bus.TraceId) && !string.IsNullOrEmpty(bus.ResultIndex))
                        {
                            _cache.TryGetValue($"bus_seats_{bus.TraceId}_{bus.ResultIndex}", out layoutMap);
                        }

                        var missingLayoutSeats = requestedSeatCodes
                            .Where(seat => layoutMap == null || 
                                           !layoutMap.TryGetValue(seat, out var layoutSeat) || 
                                           string.IsNullOrWhiteSpace(layoutSeat.SeatType))
                            .ToList();

                        if (missingLayoutSeats.Any())
                        {
                            throw new InvalidOperationException($"Authoritative seat layout information is unavailable for seat(s): {string.Join(", ", missingLayoutSeats)}. Please refresh the seat layout and block again.");
                        }

                        // ========================================
                        // CENTRALIZED PRICING ENGINE
                        // ========================================
                        var traceId = request.TraceId ?? string.Empty;
                        var blockedSeats = await dbContext.BusBlockedSeatPrices
                            .Where(x => x.TraceId == traceId)
                            .ToListAsync();

                        var missingBlockedSeats = requestedSeatCodes
                            .Where(seat => !blockedSeats.Any(b => b.SeatName.Equals(seat, StringComparison.OrdinalIgnoreCase) && b.BaseFare > 0))
                            .ToList();

                        if (missingBlockedSeats.Any())
                        {
                            throw new InvalidOperationException($"Authoritative blocked seat pricing is unavailable for seat(s): {string.Join(", ", missingBlockedSeats)}. Please refresh and block the seats again.");
                        }

                        var seatPreviews = request.Passengers
                            .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                            .Select(p => {
                                var seatCode = p.SeatNumber!.Trim();
                                var blockedSeat = blockedSeats
                                    .OrderByDescending(b => b.Id)
                                    .First(b => b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));
                                var layoutSeat = layoutMap![seatCode];
                                return new PickNBook.Api.Models.DTOs.SeatPreviewDto 
                                { 
                                    SeatCode = seatCode, 
                                    BaseFare = blockedSeat.BaseFare, 
                                    SeatType = layoutSeat.SeatType, // 100% authoritative from layout
                                    ExternalGst = blockedSeat.GstAmount 
                                };
                            })
                            .ToList();

                        var seatCodes = requestedSeatCodes;
                        var validationContext = await _couponContextBuilder.BuildContextAsync(
                            bus.TraceId,
                            bus.ResultIndex,
                            seatCodes,
                            bus,
                            seatPreviews);

                        var pricing = await _promotionEngine.CalculateAsync(
                            bus,
                            seatPreviews,
                            request.CouponCode,
                            request.PromotionId,
                            int.Parse(userId!),
                            request.SelectedFeaturedOfferId,
                            validationContext);

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
                            DiscountAmountInr = pricing.TotalDiscount,
                            ConvenienceFeeInr = pricing.ConvenienceFee,
                            CouponCode = pricing.AppliedPromotionCode ?? pricing.AutoPromotionCode,
                            AppliedPromotionId = null,
                            AppliedPromotionCode = pricing.AppliedPromotionCode,
                            AppliedPromotionType = pricing.AppliedPromotionType ?? pricing.DiscountSource,
                            AppliedFeaturedOfferId = null,
                            AppliedFeaturedOfferTitle = null,
                            FeaturedOfferDiscountAmount = 0m,
                            AutoPromotionId = null,
                            AutoPromotionCode = pricing.AutoPromotionCode,
                            DiscountSource = pricing.DiscountSource,
                            Status = BusBookingStatus.BookingInProgress,
                            FinancialStatus = "DEDUCT_PENDING",
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
                            var autoCoupon = await dbContext.BusCoupons.FirstOrDefaultAsync(x => x.CouponCode == pricing.AutoPromotionCode);
                            var autoUsage = new BusCouponUsage
                            {
                                BusCouponId = autoCoupon?.Id,
                                BusReservationId = reservation.Id,
                                UserId = userId!,
                                CouponCode = pricing.AutoPromotionCode,
                                CouponType = autoCoupon?.CouponType ?? "Fixed",
                                CouponValue = autoCoupon?.Value ?? pricing.AutoDiscountAmount,
                                CouponAmountInr = pricing.AutoDiscountAmount,
                                TotalFareInr = pricing.GrandTotal,
                                BookingStatus = BusBookingStatus.BookingInProgress,
                                UsedAtUtc = DateTime.UtcNow
                            };
                            dbContext.BusCouponUsages.Add(autoUsage);

                            await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE bus_coupons
                                SET UsedCount = UsedCount + 1
                                WHERE CouponCode = {pricing.AutoPromotionCode}
                                AND (UseLimit = 0 OR UsedCount < UseLimit)
                            ");
                        }

                        if ((pricing.CouponDiscountAmount > 0 || pricing.ManualDiscountAmount > 0) && !string.IsNullOrEmpty(pricing.AppliedPromotionCode))
                        {
                            var manualDiscountAmt = pricing.CouponDiscountAmount > 0 ? pricing.CouponDiscountAmount : pricing.ManualDiscountAmount;
                            var manualCoupon = await dbContext.BusCoupons.FirstOrDefaultAsync(x => x.CouponCode == pricing.AppliedPromotionCode);
                            var manualUsage = new BusCouponUsage
                            {
                                BusCouponId = manualCoupon?.Id,
                                BusReservationId = reservation.Id,
                                UserId = userId!,
                                CouponCode = pricing.AppliedPromotionCode,
                                CouponType = manualCoupon?.CouponType ?? "Fixed",
                                CouponValue = manualCoupon?.Value ?? manualDiscountAmt,
                                CouponAmountInr = manualDiscountAmt,
                                TotalFareInr = pricing.GrandTotal,
                                BookingStatus = BusBookingStatus.BookingInProgress,
                                UsedAtUtc = DateTime.UtcNow
                            };
                            dbContext.BusCouponUsages.Add(manualUsage);

                            var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE bus_coupons
                                SET UsedCount = UsedCount + 1
                                WHERE CouponCode = {pricing.AppliedPromotionCode}
                                AND (UseLimit = 0 OR UsedCount < UseLimit)
                            ");
                            if (rows == 0 && manualCoupon != null && manualCoupon.UseLimit > 0)
                            {
                                throw new Exception($"Coupon '{pricing.AppliedPromotionCode}' usage limit reached concurrently.");
                            }
                        }

                        var passengers = new List<BusReservationPassenger>();
                        _cache.TryGetValue($"bus_block_passengers_{request.TraceId}_{request.ResultIndex}", out List<SrdvBusPassengerDto>? cachedBlockPax);
                        if (cachedBlockPax == null)
                        {
                            _cache.TryGetValue($"bus_block_passengers_{request.TraceId}_{compositeResultIndex}", out cachedBlockPax);
                        }

                        for (int pIdx = 0; pIdx < normalizedPassengers.Count; pIdx++)
                        {
                            var p = normalizedPassengers[pIdx];
                            var seatCode = p.SeatNumber!.Trim();
                            var layoutSeat = (layoutMap != null && layoutMap.TryGetValue(seatCode, out var ls)) ? ls : null;
                            var blockedSeat = blockedSeats.OrderByDescending(b => b.Id).FirstOrDefault(b => b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));
                            var matchingBlockPax = cachedBlockPax?.FirstOrDefault(bp => bp.SeatName != null && bp.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));

                            string pFullName = p.FullName.Trim();
                            string? pTitle = !string.IsNullOrWhiteSpace(p.Title) ? p.Title : matchingBlockPax?.Title;
                            string? pFirst = !string.IsNullOrWhiteSpace(p.FirstName) ? p.FirstName : matchingBlockPax?.FirstName;
                            string? pLast = !string.IsNullOrWhiteSpace(p.LastName) ? p.LastName : matchingBlockPax?.LastName;

                            if (string.IsNullOrWhiteSpace(pFirst))
                            {
                                if (pFullName.Contains(' '))
                                {
                                    var parts = pFullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                                    pFirst = parts[0];
                                    pLast = parts.Length > 1 ? parts[1] : "";
                                }
                                else
                                {
                                    pFirst = pFullName;
                                    pLast = "";
                                }
                            }

                            bool isLead = p.LeadPassenger == true;
                            int seatIndex = p.SeatIndex ?? matchingBlockPax?.SeatIndex ?? (pIdx + 1);

                            passengers.Add(new BusReservationPassenger
                            {
                                BusReservationId = reservation.Id,
                                FullName = pFullName,
                                Title = pTitle,
                                FirstName = pFirst,
                                LastName = pLast ?? "",
                                LeadPassenger = isLead,
                                SeatIndex = seatIndex,
                                Gender = p.Gender,
                                SeatNumber = seatCode,
                                BaseFareInr = blockedSeat!.BaseFare,
                                SeatType = layoutSeat!.SeatType,
                                Age = p.Age,
                                PublishedFareInr = blockedSeat.PublishedFare > 0 ? blockedSeat.PublishedFare : blockedSeat.BaseFare,
                                GstAmountInr = blockedSeat.GstAmount,
                                TaxInr = blockedSeat.GstAmount,
                                OfferedFareInr = blockedSeat.GrandTotal > 0 ? (blockedSeat.GrandTotal - blockedSeat.GstAmount) : (blockedSeat.PublishedFare > 0 ? blockedSeat.PublishedFare - blockedSeat.DiscountAmount : blockedSeat.BaseFare)
                            });
                        }
                        dbContext.BusReservationPassengers.AddRange(passengers);

                        await TrackBusRouteBookingCounterAsync(bus.FromCity, bus.ToCity);

                        await dbContext.SaveChangesAsync();

                        // =======================================================
                        // VALIDATE WALLET BALANCE (CRITICAL: NO DEBIT IN PHASE 1!)
                        // =======================================================
                        if (User?.IsInRole(AuthRoles.Agent) == true && string.Equals(request.PaymentMethod, "Agent Wallet", StringComparison.OrdinalIgnoreCase))
                        {
                            var agentUser = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == int.Parse(userId!));
                            if (agentUser == null || agentUser.WalletBalance < reservation.TotalPriceInr)
                            {
                                throw new BusBookingException(7030, "Insufficient agent wallet balance.");
                            }
                        }

                        reservation.FinancialStatus = "DEDUCT_PENDING";
                        await dbContext.SaveChangesAsync();

                        await transaction.CommitAsync();

                        return (bus, reservation, passengers, pricing);
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });

                // ========================================
                // PHASE 2: EXTERNAL API CALL OUTSIDE DB TX
                // ========================================
                SrdvBusBookingResponseDto? srdvRes = null;
                bool isAmbiguousOutcome = false;
                string? explicitFailureMessage = null;

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

                    var blockKeyToUse = !string.IsNullOrWhiteSpace(request.BlockKey)
                        ? request.BlockKey
                        : (_cache.TryGetValue($"bus_blockkey_{srdvReq.TraceId}_{srdvReq.ResultIndex}", out string? cachedBk) 
                            ? cachedBk 
                            : (_cache.TryGetValue($"bus_blockkey_{srdvReq.TraceId}_{compositeResultIndex}", out string? cachedCompBk) ? cachedCompBk : ""));

                    try
                    {
                        srdvRes = await _srdvBusService.BookBusAsync(srdvReq, blockKeyToUse ?? "");
                    }
                    catch (Exception bookEx)
                    {
                        isAmbiguousOutcome = true;
                        logger.LogWarning(bookEx, "SRDV BookBusAsync call threw exception / timed out for TraceId {TraceId}. Ambiguous outcome detected.", srdvReq.TraceId);
                    }

                    // Case A: Explicit supplier rejection — DO NOT call BookingDetails unnecessarily
                    if (srdvRes != null && srdvRes.IsExplicitSupplierRejection)
                    {
                        explicitFailureMessage = !string.IsNullOrWhiteSpace(srdvRes.ErrorMessage)
                            ? srdvRes.ErrorMessage
                            : "Supplier rejected booking request";
                    }
                    // Case B: Ambiguous Book outcome (timeout, connection failure, or incomplete/malformed response without explicit rejection)
                    else if (isAmbiguousOutcome || srdvRes == null || (!srdvRes.Success && !srdvRes.IsExplicitSupplierRejection))
                    {
                        isAmbiguousOutcome = true;
                        SrdvBusBookingDetailsResponseDto? details = null;
                        try
                        {
                            details = await _srdvBusService.GetBookingDetailsAsync(srdvReq.TraceId);
                        }
                        catch (Exception detailsEx)
                        {
                            logger.LogWarning(detailsEx, "BookingDetails recovery query failed for TraceId {TraceId}.", srdvReq.TraceId);
                        }

                        if (details != null && details.Success && details.Result != null)
                        {
                            var status = details.Result.BookingStatus?.Trim();
                            bool isConfirmed = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(status, "Confirmed", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(status, "Booked", StringComparison.OrdinalIgnoreCase);

                            bool isExplicitFailure = string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                                || (details.Error != null && details.Error.ErrorCode > 0)
                                || details.Result.ErrorCode > 0;

                            if (isConfirmed)
                            {
                                logger.LogInformation("BookingDetails confirmed successful booking for TraceId {TraceId}. Status: {Status}, TicketNo: {TicketNo}, PNR: {Pnr}",
                                    srdvReq.TraceId, status, details.Result.TicketNo, details.Result.TravelOperatorPNR);

                                srdvRes = new SrdvBusBookingResponseDto
                                {
                                    Success = true,
                                    ErrorCode = 0,
                                    SrdvBookingId = details.Result.BookingId?.ToString(),
                                    TicketNo = details.Result.TicketNo,
                                    TravelOperatorPNR = details.Result.TravelOperatorPNR,
                                    ResponseJson = details.ResponseJson
                                };
                                isAmbiguousOutcome = false;
                            }
                            else if (isExplicitFailure)
                            {
                                explicitFailureMessage = !string.IsNullOrWhiteSpace(details.Result.ErrorMessage)
                                    ? details.Result.ErrorMessage
                                    : (!string.IsNullOrWhiteSpace(details.Error?.ErrorMessage) ? details.Error.ErrorMessage : $"Booking rejected by provider with status {status}");
                                isAmbiguousOutcome = false;
                            }
                        }
                    }
                }

                // ========================================
                // PHASE 3: LOCAL DATABASE (OUTCOME PERSISTENCE)
                // ========================================
                if (isAmbiguousOutcome && (srdvRes == null || !srdvRes.Success) && explicitFailureMessage == null)
                {
                    logger.LogCritical("SRDV Booking outcome for TraceId {TraceId} is inconclusive after recovery. Flagging MANUAL_CHECK_REQUIRED.", bus.TraceId);
                    await strategy.ExecuteAsync(async () =>
                    {
                        await using var ambTx = await dbContext.Database.BeginTransactionAsync();
                        try
                        {
                            reservation.Status = BusBookingStatus.ManualCheckRequired;
                            reservation.FinancialStatus = "DEDUCT_PENDING";
                            await dbContext.SaveChangesAsync();
                            await ambTx.CommitAsync();
                        }
                        catch
                        {
                            await ambTx.RollbackAsync();
                            throw;
                        }
                    });

                    return BadRequest(new
                    {
                        message = "MANUAL_CHECK_REQUIRED: Booking outcome is ambiguous and awaiting reconciliation.",
                        Error = new
                        {
                            ErrorCode = 7033,
                            ErrorMessage = "MANUAL_CHECK_REQUIRED: Booking outcome is ambiguous and awaiting reconciliation."
                        }
                    });
                }

                if (explicitFailureMessage != null || (srdvRes != null && !srdvRes.Success))
                {
                    var failMsg = explicitFailureMessage ?? srdvRes?.ErrorMessage ?? "SRDV Booking Failed";
                    await strategy.ExecuteAsync(async () =>
                    {
                        await using var failTx = await dbContext.Database.BeginTransactionAsync();
                        try
                        {
                            reservation.Status = BusBookingStatus.Failed;
                            reservation.CancellationReason = failMsg;
                            // Release/void pending financial reservation without wallet credit (funds were never debited)
                            reservation.FinancialStatus = "VOIDED";

                            if (!string.IsNullOrWhiteSpace(reservation.CouponCode))
                            {
                                await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                    UPDATE bus_coupons
                                    SET UsedCount = CASE WHEN UsedCount > 0 THEN UsedCount - 1 ELSE 0 END
                                    WHERE CouponCode = {reservation.CouponCode}
                                ");
                            }

                            var couponUsages = await dbContext.BusCouponUsages.Where(u => u.BusReservationId == reservation.Id).ToListAsync();
                            foreach (var cu in couponUsages)
                            {
                                cu.BookingStatus = BusBookingStatus.Failed;
                            }

                            await dbContext.SaveChangesAsync();
                            await failTx.CommitAsync();
                        }
                        catch
                        {
                            await failTx.RollbackAsync();
                            throw;
                        }
                    });

                    return BadRequest(new
                    {
                        message = $"SRDV Booking Failed: {failMsg}",
                        Error = new
                        {
                            ErrorCode = 7032,
                            ErrorMessage = $"SRDV Booking Failed: {failMsg}"
                        }
                    });
                }

                // Authoritative Supplier Confirmation: Commit Phase 3 in dedicated DB transaction
                await strategy.ExecuteAsync(async () =>
                {
                    await using var phase3Tx = await dbContext.Database.BeginTransactionAsync();
                    try
                    {
                        reservation.Status = BusBookingStatus.Success;
                        if (srdvRes != null)
                        {
                            reservation.SrdvBookingId = srdvRes.SrdvBookingId;
                            reservation.SrdvBookingResponseJson = srdvRes.ResponseJson;
                            reservation.SrdvTicketNo = srdvRes.TicketNo;
                            reservation.Pnr = srdvRes.TravelOperatorPNR ?? srdvRes.TicketNo ?? reservation.Pnr;
                            bus.TraceId = !string.IsNullOrWhiteSpace(request.TraceId) ? request.TraceId : bus.TraceId;
                        }

                        var successCouponUsages = await dbContext.BusCouponUsages.Where(u => u.BusReservationId == reservation.Id).ToListAsync();
                        foreach (var cu in successCouponUsages)
                        {
                            cu.BookingStatus = BusBookingStatus.Success;
                        }

                        await dbContext.SaveChangesAsync();
                        await phase3Tx.CommitAsync();
                    }
                    catch
                    {
                        await phase3Tx.RollbackAsync();
                        throw;
                    }
                });

                // =========================================================================
                // PHASE 4: FINANCIAL MOVEMENT (ONLY AFTER PHASE 3 CONFIRMATION COMMITTED!)
                // =========================================================================
                if (User?.IsInRole(AuthRoles.Agent) == true && string.Equals(request.PaymentMethod, "Agent Wallet", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var walletService = HttpContext.RequestServices.GetRequiredService<IAgentWalletService>();
                        await walletService.DebitWalletForBookingAsync(
                            int.Parse(userId!),
                            reservation.TotalPriceInr,
                            reservation.BookingReference,
                            "Bus",
                            $"Bus Booking - {bus.FromCity} to {bus.ToCity} ({bus.OperatorName}) - Ref: {reservation.BookingReference}"
                        );
                        reservation.FinancialStatus = "DEDUCTED";
                    }
                    catch (Exception wEx)
                    {
                        logger.LogError(wEx, "Wallet debit failed after confirmed supplier booking {BookingRef}", reservation.BookingReference);
                        // DO NOT mark supplier booking FAILED - it is already confirmed on provider!
                        reservation.FinancialStatus = "DEBIT_FAILED_MANUAL_RECOVERY";
                    }
                    await dbContext.SaveChangesAsync();
                }
                else
                {
                    reservation.FinancialStatus = "DEDUCTED";
                    await dbContext.SaveChangesAsync();
                }

                var mappedResponse = MapBusReservation(reservation, bus, passengers);
                var completedResponseJson = System.Text.Json.JsonSerializer.Serialize(mappedResponse);
                _cache.Set(idempotencyKey, completedResponseJson, TimeSpan.FromHours(1));
                if (!string.Equals(compositeResultIndex, request.ResultIndex, StringComparison.OrdinalIgnoreCase))
                {
                    _cache.Set($"bus_book_completed_{request.TraceId}_{request.ResultIndex}", completedResponseJson, TimeSpan.FromHours(1));
                }

                try
                {
                    var reservationId = reservation.Id;
                    var targetBusId = bus.Id;
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
                        var pList = await scopedContext.BusReservationPassengers.Where(p => p.BusReservationId == reservationId).ToListAsync(ct);

                        if (res != null && b != null)
                        {
                            var seatNumbers = string.Join(", ",
                                pList.Select(x => x.SeatNumber).Where(x => !string.IsNullOrWhiteSpace(x)));

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
                                        Passengers = pList.Select(p => new BusPassengerSeatDto
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
                        reservation.BookingReference);
                }

                return CreatedAtAction(
                    nameof(GetBusBookingById),
                    new { bookingId = reservation.Id },
                    mappedResponse
                );
            }
            catch (Exception ex)
            {
                var busEx = ex as BusBookingException ?? ex.GetBaseException() as BusBookingException;
                if (busEx != null)
                {
                    return BadRequest(new
                    {
                        message = busEx.Message,
                        Error = new
                        {
                            ErrorCode = busEx.ErrorCode,
                            ErrorMessage = busEx.Message
                        }
                    });
                }
                if (ex is InvalidOperationException)
                {
                    return BadRequest(new { message = ex.Message });
                }
                return BadRequest(new { message = ex.Message });
            }
            finally
            {
                _cache.Remove(lockKey);
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

            try
            {
                var booking = await dbContext.BusReservations
                    .Include(x => x.BusBooking)
                    .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

                if (booking is null || booking.BusBooking is null)
                    return NotFound("Booking not found.");

                if (booking.Status == "Cancelled" || booking.Status == BusBookingStatus.Cancelled)
                    return BadRequest("Already cancelled.");

                // Prevent cancellation after departure
                if (booking.BusBooking.DepartureTime <= DateTime.UtcNow)
                {
                    return BadRequest("Cannot cancel ticket after bus departure.");
                }

                // 🔥 GET PASSENGERS
                var passengers = await dbContext.BusReservationPassengers
                    .Where(x => x.BusReservationId == booking.Id)
                    .ToListAsync();

                var activePassengers = passengers.Where(x => !x.IsCancelled).ToList();
                if (activePassengers.Count == 0)
                    return BadRequest("Already cancelled.");

                var seatNumbers = activePassengers
                    .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                    .Select(x => x.SeatNumber!)
                    .ToList();

                decimal srdvCancellationCharge = 0m;
                decimal srdvRefundAmount = 0m;
                bool requiresManualReview = false;
                bool cancellationConfirmed = false;
                long? cancelTraceId = null;
                SrdvBusCancelResponseDto? v9Result = null;

                if (booking.BusBooking.BusNumber.StartsWith("SRDV-") && !string.IsNullOrEmpty(booking.BusBooking.TraceId))
                {
                    if (seatNumbers.Any())
                    {
                        string actualTraceId = booking.BusBooking.TraceId ?? string.Empty;
                        if (!long.TryParse(actualTraceId, out var parsedTraceId) || parsedTraceId <= 0)
                        {
                            return BadRequest("Invalid TraceId on booking.");
                        }

                        cancelTraceId = parsedTraceId;
                        var cancelRemarks = string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason.Trim();
                        v9Result = await _srdvBusService.CancelTicketV9Async(
                            parsedTraceId,
                            seatNumbers,
                            cancelRemarks);

                        if (v9Result.Success)
                        {
                            try
                            {
                                var details = await _srdvBusService.GetBookingDetailsAsync(actualTraceId);
                                var reconciled = TryReconcileCancellationFromDetails(details, seatNumbers, v9Result.CancelId);
                                if (reconciled.IsConfirmed)
                                {
                                    cancellationConfirmed = true;
                                    logger.LogInformation("BookingDetails confirmed full cancellation for seats {Seats}, TraceId {TraceId}.", string.Join(",", seatNumbers), actualTraceId);
                                    srdvCancellationCharge = reconciled.CancellationCharge;
                                    srdvRefundAmount = reconciled.RefundAmount;
                                }
                                else
                                {
                                    srdvCancellationCharge = v9Result.CancellationCharge;
                                    srdvRefundAmount = v9Result.RefundAmount;
                                }
                            }
                            catch (Exception detailsEx)
                            {
                                logger.LogWarning(detailsEx, "BookingDetails reconciliation lookup failed after successful cancel initiation for TraceId {TraceId}.", actualTraceId);
                                srdvCancellationCharge = v9Result.CancellationCharge;
                                srdvRefundAmount = v9Result.RefundAmount;
                            }
                        }
                        else if (v9Result.IsExplicitSupplierRejection)
                        {
                            return BadRequest($"SRDV Provider Error: {v9Result.ErrorMessage}");
                        }
                        else
                        {
                            logger.LogWarning("SRDV Cancel produced ambiguous response/timeout for TraceId {TraceId}. Reconciling via BookingDetails.", actualTraceId);
                            bool confirmed = false;
                            try
                            {
                                var details = await _srdvBusService.GetBookingDetailsAsync(actualTraceId);
                                var reconciled = TryReconcileCancellationFromDetails(details, seatNumbers);
                                if (reconciled.IsConfirmed)
                                {
                                    confirmed = true;
                                    cancellationConfirmed = true;
                                    srdvCancellationCharge = reconciled.CancellationCharge;
                                    srdvRefundAmount = reconciled.RefundAmount;
                                    logger.LogInformation("BookingDetails confirmed full cancellation following ambiguous cancel for TraceId {TraceId}.", actualTraceId);
                                }
                            }
                            catch (Exception detailsEx)
                            {
                                logger.LogWarning(detailsEx, "BookingDetails reconciliation failed following ambiguous cancel for TraceId {TraceId}.", actualTraceId);
                            }

                            if (!confirmed)
                            {
                                requiresManualReview = true;
                            }
                        }
                    }
                }
                else
                {
                    cancellationConfirmed = true;
                }

                var strategy = dbContext.Database.CreateExecutionStrategy();
                var executionResult = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync();

                    var curBooking = await dbContext.BusReservations
                        .Include(x => x.BusBooking)
                        .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

                    if (curBooking is null || curBooking.BusBooking is null)
                        throw new Exception("Booking not found.");

                    var curPassengers = await dbContext.BusReservationPassengers
                        .Where(x => x.BusReservationId == curBooking.Id)
                        .ToListAsync();

                    var curActive = curPassengers.Where(x => !x.IsCancelled).ToList();

                    // Dynamic SRDV Cancellation Policy
                    var refundInput = new PickNBook.Api.Models.DTOs.RefundCalculationInput
                    {
                        OriginalCustomerPaid = curBooking.TotalPriceInr,
                        SupplierAmount = curBooking.NetFareInr,
                        MarkupAmount = curBooking.MarkupAmountInr,
                        DiscountAmount = curBooking.CouponDiscountAmountInr + curBooking.AutoDiscountAmountInr + curBooking.FeaturedOfferDiscountAmount,
                        ConvenienceFee = curBooking.ConvenienceFeeInr,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        SupplierRefundAmount = srdvRefundAmount
                    };

                    var calculatedRefund = refundCalculator.CalculateCustomerRefund(refundInput);

                    if (requiresManualReview)
                    {
                        curBooking.Status = BusBookingStatus.ManualCheckRequired;
                    }
                    else if (cancellationConfirmed)
                    {
                        foreach (var p in curActive)
                        {
                            p.IsCancelled = true;
                            p.CancelledAtUtc = DateTime.UtcNow;
                        }

                        curBooking.Status = "Cancelled";
                        curBooking.CancelledAtUtc = DateTime.UtcNow;
                        curBooking.CancellationReason = string.IsNullOrWhiteSpace(reason)
                            ? "Cancelled by user"
                            : reason.Trim();

                        curBooking.CancellationChargeInr = calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                        curBooking.RefundAmountInr = calculatedRefund.FinalCustomerRefundAmount;
                        curBooking.FinancialStatus = calculatedRefund.FinalCustomerRefundAmount > 0 ? "PENDING_REFUND" : "NO_REFUND";

                        var usage = await dbContext.BusCouponUsages
                            .FirstOrDefaultAsync(x => x.BusReservationId == curBooking.Id);

                        if (usage != null)
                        {
                            usage.BookingStatus = "Cancelled";
                            usage.UsedAtUtc = DateTime.UtcNow;
                        }

                        if (!string.IsNullOrWhiteSpace(curBooking.CouponCode))
                        {
                            await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE bus_coupons
        SET UsedCount = CASE 
            WHEN UsedCount > 0 THEN UsedCount - 1 
            ELSE 0 
        END
        WHERE CouponCode = {curBooking.CouponCode}
    ");
                        }
                    }
                    else
                    {
                        curBooking.Status = BusBookingStatus.CancelInProcess;
                    }

                    if (v9Result != null)
                    {
                        curBooking.ProviderCancelId = v9Result.CancelId;
                        curBooking.SupplierCancelId = v9Result.SupplierCancelId;
                    }

                    string auditStatus = requiresManualReview
                        ? "MANUAL_CHECK_REQUIRED"
                        : (cancellationConfirmed
                            ? ((calculatedRefund.FinalCustomerRefundAmount > 0) ? "PendingReview" : "Completed")
                            : "CANCEL_IN_PROCESS");

                    string refundAuditStatus = requiresManualReview
                        ? "MANUAL_CHECK_REQUIRED"
                        : (cancellationConfirmed
                            ? (calculatedRefund.FinalCustomerRefundAmount > 0 ? "PENDING" : "NOT_REQUIRED")
                            : "PENDING");

                    var activeSeatNames = curActive
                        .Select(x => x.SeatNumber!.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var requestedSeatNames = (seatNumbers ?? new List<string>())
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var calculatedCancellationType = (requestedSeatNames.Count == 0 || requestedSeatNames.SetEquals(activeSeatNames))
                        ? "FULL"
                        : "PARTIAL";

                    var cancellationAudit = new PickNBook.Api.Models.Entities.BookingCancellation
                    {
                        BookingType = "Bus",
                        BookingReference = curBooking.BookingReference,
                        UserId = curBooking.UserId,
                        CreatedAtUtc = DateTime.UtcNow,
                        OriginalCustomerPaid = curBooking.TotalPriceInr,
                        SupplierAmount = curBooking.NetFareInr,
                        MarkupAmount = curBooking.MarkupAmountInr,
                        ConvenienceFee = curBooking.ConvenienceFeeInr,
                        DiscountAmount = curBooking.CouponDiscountAmountInr + curBooking.AutoDiscountAmountInr + curBooking.FeaturedOfferDiscountAmount,
                        SupplierRefundAmount = srdvRefundAmount,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        MarkupRefunded = calculatedRefund.MarkupRefunded,
                        FeeRefunded = calculatedRefund.FeeRefunded,
                        CouponForfeited = calculatedRefund.CouponForfeited,
                        CustomerRefundAmount = cancellationConfirmed ? calculatedRefund.FinalCustomerRefundAmount : 0m,
                        Status = auditStatus,
                        CancellationType = calculatedCancellationType,
                        RefundStatus = refundAuditStatus,
                        TraceId = cancelTraceId,
                        ProviderCancelId = v9Result?.CancelId,
                        SupplierCancelId = v9Result?.SupplierCancelId,
                        SeatNamesJson = JsonSerializer.Serialize(seatNumbers)
                    };
                    dbContext.BookingCancellations.Add(cancellationAudit);
                    await dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultPassengers = await dbContext.BusReservationPassengers
                        .AsNoTracking()
                        .Where(x => x.BusReservationId == curBooking.Id)
                        .OrderBy(x => x.Id)
                        .ToListAsync();

                    var mapped = MapBusReservation(curBooking, curBooking.BusBooking, resultPassengers);
                    return new
                    {
                        Result = mapped,
                        CancelledIds = cancellationConfirmed ? curActive.Select(x => x.Id).ToList() : new List<int>(),
                        RefundAmount = cancellationConfirmed ? calculatedRefund.FinalCustomerRefundAmount : 0m
                    };
                });

                if (executionResult.CancelledIds.Count > 0)
                {
                    await TrySendBusCancellationNotificationsAsync(bookingId, userId!, executionResult.CancelledIds, executionResult.RefundAmount);
                }

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

            try
            {
                var booking = await dbContext.BusReservations
                    .Include(x => x.BusBooking)
                    .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

                if (booking is null || booking.BusBooking is null)
                    return NotFound("Booking not found.");

                if (booking.Status == "Cancelled" || booking.Status == BusBookingStatus.Cancelled)
                    return BadRequest("Already cancelled.");

                // Prevent cancellation after departure
                if (booking.BusBooking.DepartureTime <= DateTime.UtcNow)
                {
                    return BadRequest("Cannot cancel ticket after bus departure.");
                }

                // 🔥 GET PASSENGERS
                var passengers = await dbContext.BusReservationPassengers
                    .Where(x => x.BusReservationId == booking.Id)
                    .ToListAsync();

                var targetPassengers = passengers
                    .Where(x => request.PassengerIds.Contains(x.Id))
                    .ToList();

                if (targetPassengers.Count != request.PassengerIds.Count)
                    return BadRequest("One or more passenger IDs are invalid for this reservation.");

                if (targetPassengers.Any(x => x.IsCancelled))
                    return BadRequest("One or more passenger tickets are already cancelled.");

                var activePassengersCount = passengers.Count(x => !x.IsCancelled);
                if (targetPassengers.Count > activePassengersCount)
                    return BadRequest("Cannot cancel more passengers than currently active.");

                var seatNumbers = targetPassengers
                    .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                    .Select(x => x.SeatNumber!)
                    .ToList();

                decimal srdvCancellationCharge = 0m;
                decimal srdvRefundAmount = 0m;
                bool requiresManualReview = false;
                bool cancellationConfirmed = false;
                long? cancelTraceId = null;
                SrdvBusCancelResponseDto? v9Result = null;

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
                            return BadRequest("Partial cancellation is not permitted for this bus booking. You must cancel the entire booking.");
                        }

                        var isLeadPassengerCancelled = targetPassengers.Any(p => p.Id == passengers.First().Id);

                        if (isLeadPassengerCancelled && activePassengersCount > targetPassengers.Count)
                        {
                            return BadRequest("Cancelling the lead passenger will automatically cancel the entire booking on SRDV. If you want to cancel the entire booking, please use the Full Cancel button instead.");
                        }

                        var targetSeats = targetPassengers
                            .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                            .Select(x => x.SeatNumber!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (targetSeats.Any())
                        {
                            string actualTraceId = booking.BusBooking.TraceId ?? string.Empty;
                            if (!long.TryParse(actualTraceId, out var parsedTraceId) || parsedTraceId <= 0)
                            {
                                return BadRequest("Invalid TraceId on booking.");
                            }

                            cancelTraceId = parsedTraceId;
                            var partialRemarks = string.IsNullOrWhiteSpace(request.Reason) ? "Partial passenger cancellation" : request.Reason.Trim();
                            v9Result = await _srdvBusService.CancelTicketV9Async(
                                parsedTraceId,
                                targetSeats,
                                partialRemarks);

                            if (v9Result.Success)
                            {
                                try
                                {
                                    var details = await _srdvBusService.GetBookingDetailsAsync(actualTraceId);
                                    var reconciled = TryReconcileCancellationFromDetails(details, targetSeats, v9Result.CancelId);
                                    if (reconciled.IsConfirmed)
                                    {
                                        cancellationConfirmed = true;
                                        logger.LogInformation("BookingDetails confirmed partial cancellation for seats {Seats}, TraceId {TraceId}.", string.Join(",", targetSeats), actualTraceId);
                                        srdvCancellationCharge = reconciled.CancellationCharge;
                                        srdvRefundAmount = reconciled.RefundAmount;
                                    }
                                    else
                                    {
                                        srdvCancellationCharge = v9Result.CancellationCharge;
                                        srdvRefundAmount = v9Result.RefundAmount;
                                    }
                                }
                                catch (Exception detailsEx)
                                {
                                    logger.LogWarning(detailsEx, "BookingDetails reconciliation failed for partial cancel, TraceId {TraceId}.", actualTraceId);
                                    srdvCancellationCharge = v9Result.CancellationCharge;
                                    srdvRefundAmount = v9Result.RefundAmount;
                                }
                            }
                            else if (v9Result.IsExplicitSupplierRejection)
                            {
                                return BadRequest($"SRDV Provider Error: {v9Result.ErrorMessage}");
                            }
                            else
                            {
                                logger.LogWarning("SRDV Cancel produced ambiguous outcome for partial cancel, TraceId {TraceId}. Checking BookingDetails.", actualTraceId);
                                bool confirmed = false;
                                try
                                {
                                    var details = await _srdvBusService.GetBookingDetailsAsync(actualTraceId);
                                    var reconciled = TryReconcileCancellationFromDetails(details, targetSeats);
                                    if (reconciled.IsConfirmed)
                                    {
                                        confirmed = true;
                                        cancellationConfirmed = true;
                                        srdvCancellationCharge = reconciled.CancellationCharge;
                                        srdvRefundAmount = reconciled.RefundAmount;
                                        logger.LogInformation("BookingDetails confirmed partial cancellation following ambiguous outcome for TraceId {TraceId}.", actualTraceId);
                                    }
                                }
                                catch (Exception detailsEx)
                                {
                                    logger.LogWarning(detailsEx, "BookingDetails lookup failed after ambiguous partial cancel for TraceId {TraceId}.", actualTraceId);
                                }

                                if (!confirmed)
                                {
                                    requiresManualReview = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        cancellationConfirmed = true;
                    }
                }
                else
                {
                    cancellationConfirmed = true;
                }

                var strategy = dbContext.Database.CreateExecutionStrategy();
                var executionResult = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync();

                    var curBooking = await dbContext.BusReservations
                        .Include(x => x.BusBooking)
                        .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId);

                    if (curBooking is null || curBooking.BusBooking is null)
                        throw new Exception("Booking not found.");

                    var curPassengers = await dbContext.BusReservationPassengers
                        .Where(x => x.BusReservationId == curBooking.Id)
                        .ToListAsync();

                    var curTarget = curPassengers
                        .Where(x => request.PassengerIds.Contains(x.Id))
                        .ToList();

                    var curActiveCount = curPassengers.Count(x => !x.IsCancelled);

                    if (cancellationConfirmed)
                    {
                        foreach (var p in curTarget)
                        {
                            p.IsCancelled = true;
                            p.CancelledAtUtc = DateTime.UtcNow;
                        }
                    }

                    // ── Dynamic SRDV Cancellation Policy ──
                    decimal proportion = (decimal)curTarget.Count / (curBooking.SeatsBooked > 0 ? curBooking.SeatsBooked : 1);

                    var refundInput = new PickNBook.Api.Models.DTOs.RefundCalculationInput
                    {
                        OriginalCustomerPaid = curBooking.TotalPriceInr * proportion,
                        SupplierAmount = curBooking.NetFareInr * proportion,
                        MarkupAmount = curBooking.MarkupAmountInr * proportion,
                        DiscountAmount = (curBooking.CouponDiscountAmountInr + curBooking.AutoDiscountAmountInr + curBooking.FeaturedOfferDiscountAmount) * proportion,
                        ConvenienceFee = curBooking.ConvenienceFeeInr * proportion,
                        SupplierCancellationCharge = srdvCancellationCharge,
                        SupplierRefundAmount = srdvRefundAmount
                    };

                    var calculatedRefund = refundCalculator.CalculateCustomerRefund(refundInput);

                    if (cancellationConfirmed)
                    {
                        curBooking.CancellationChargeInr = (curBooking.CancellationChargeInr ?? 0m) + calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                        curBooking.RefundAmountInr = (curBooking.RefundAmountInr ?? 0m) + calculatedRefund.FinalCustomerRefundAmount;
                        curBooking.FinancialStatus = calculatedRefund.FinalCustomerRefundAmount > 0 ? "PENDING_REFUND" : "NO_REFUND";
                    }

                    if (v9Result != null)
                    {
                        curBooking.ProviderCancelId = v9Result.CancelId;
                        curBooking.SupplierCancelId = v9Result.SupplierCancelId;
                    }

                    var activeSeatNames = curPassengers
                        .Where(x => !x.IsCancelled)
                        .Select(x => x.SeatNumber!.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var requestedSeatNames = curTarget
                        .Select(x => x.SeatNumber!.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    string cancellationType = requestedSeatNames.SetEquals(activeSeatNames) ? "FULL" : "PARTIAL";
                    string refundAuditStatus = requiresManualReview
                        ? "MANUAL_CHECK_REQUIRED"
                        : (cancellationConfirmed
                            ? (calculatedRefund.FinalCustomerRefundAmount > 0 ? "PENDING" : "NOT_REQUIRED")
                            : "PENDING");

                    string auditStatus = requiresManualReview
                        ? "PendingReview"
                        : (cancellationConfirmed
                            ? (calculatedRefund.FinalCustomerRefundAmount > 0 ? "PendingReview" : "Completed")
                            : "CANCEL_IN_PROCESS");

                    var cancellationAudit = new PickNBook.Api.Models.Entities.BookingCancellation
                    {
                        BookingType = "Bus",
                        BookingReference = curBooking.BookingReference,
                        UserId = curBooking.UserId,
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
                        CustomerRefundAmount = cancellationConfirmed ? calculatedRefund.FinalCustomerRefundAmount : 0m,
                        Status = auditStatus,
                        CancellationType = cancellationType,
                        RefundStatus = refundAuditStatus,
                        TraceId = cancelTraceId,
                        ProviderCancelId = v9Result?.CancelId,
                        SupplierCancelId = v9Result?.SupplierCancelId,
                        SeatNamesJson = JsonSerializer.Serialize(curTarget.Select(x => x.SeatNumber!).Where(s => !string.IsNullOrWhiteSpace(s)))
                    };
                    dbContext.BookingCancellations.Add(cancellationAudit);
                    await dbContext.SaveChangesAsync();

                    if (requiresManualReview)
                    {
                        curBooking.Status = BusBookingStatus.ManualCheckRequired;
                    }
                    else if (!cancellationConfirmed)
                    {
                        curBooking.Status = BusBookingStatus.CancelInProcess;
                    }
                    else
                    {
                        var remainingActiveCount = curActiveCount - curTarget.Count;
                        if (remainingActiveCount == 0)
                        {
                            curBooking.Status = "Cancelled";
                            curBooking.CancelledAtUtc = DateTime.UtcNow;
                            curBooking.CancellationReason = string.IsNullOrWhiteSpace(request.Reason)
                                ? "All passengers cancelled"
                                : request.Reason.Trim();

                            var usage = await dbContext.BusCouponUsages
                                .FirstOrDefaultAsync(x => x.BusReservationId == curBooking.Id);

                            if (usage != null)
                            {
                                usage.BookingStatus = "Cancelled";
                                usage.UsedAtUtc = DateTime.UtcNow;
                            }

                            if (!string.IsNullOrWhiteSpace(curBooking.CouponCode))
                            {
                                await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                                    UPDATE bus_coupons
                                    SET UsedCount = CASE 
                                        WHEN UsedCount > 0 THEN UsedCount - 1 
                                        ELSE 0 
                                        END
                                    WHERE CouponCode = {curBooking.CouponCode}
                                ");
                            }
                        }
                        else
                        {
                            curBooking.Status = BusBookingStatus.PartiallyCancelled;
                        }
                    }

                    await dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultPassengers = await dbContext.BusReservationPassengers
                        .AsNoTracking()
                        .Where(x => x.BusReservationId == curBooking.Id)
                        .OrderBy(x => x.Id)
                        .ToListAsync();

                    var mapped = MapBusReservation(curBooking, curBooking.BusBooking, resultPassengers);

                    return new
                    {
                        Result = mapped,
                        CancelledIds = cancellationConfirmed ? curTarget.Select(x => x.Id).ToList() : new List<int>(),
                        RefundAmount = cancellationConfirmed ? calculatedRefund.FinalCustomerRefundAmount : 0m
                    };
                });

                if (executionResult.CancelledIds.Count > 0)
                {
                    await TrySendBusCancellationNotificationsAsync(bookingId, userId!, executionResult.CancelledIds, executionResult.RefundAmount);
                }

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
        (BusBookingStatus.IsConfirmed(reservation.Status) || reservation.Status == "Booked") &&
        bus.DepartureTime > DateTime.UtcNow,

                TripState =
        (BusBookingStatus.IsCancelled(reservation.Status) || reservation.Status == "Cancelled")
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

            var leadCount = passengers.Count(p => p.LeadPassenger == true);
            if (leadCount != 1)
            {
                return $"Exactly one passenger must be designated as the lead passenger (found {leadCount}).";
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
                    Title = passenger.Title?.Trim(),
                    FirstName = passenger.FirstName?.Trim(),
                    LastName = passenger.LastName?.Trim(),
                    LeadPassenger = passenger.LeadPassenger == true,
                    SeatIndex = passenger.SeatIndex,
                    Gender = normalizedGender,
                    SeatNumber = normalizedSeat,
                    Age = passenger.Age,
                    BaseFare = passenger.BaseFare,
                    SeatType = passenger.SeatType,
                    ExternalGst = passenger.ExternalGst
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

        [HttpPost("v9/BookingDetails")]
        [HttpPost("booking-details")]
        public async Task<IActionResult> GetBookingDetailsV9([FromBody] BusBookingDetailsQueryRequestDto request)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to view booking details.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            if (request == null || request.TraceId <= 0)
            {
                return BadRequest(new SrdvBusBookingDetailsResponseDto
                {
                    Success = false,
                    Result = null,
                    TraceId = request?.TraceId ?? 0,
                    Error = new SrdvBusBookingDetailsErrorDto
                    {
                        ErrorCode = 7044,
                        ErrorMessage = "TraceId must be present, numeric, and greater than 0."
                    }
                });
            }

            var normalizedTraceId = request.TraceId.ToString();
            var isAdmin = User?.IsInRole(AuthRoles.Admin) == true;

            // Security & Zero-Enumeration: read local booking data strictly without supplier calls or DB mutations
            var booking = await dbContext.BusReservations
                .AsNoTracking()
                .Include(r => r.BusBooking)
                .FirstOrDefaultAsync(r => (r.BusBooking != null && r.BusBooking.TraceId == normalizedTraceId) && (isAdmin || r.UserId == userId));

            if (booking == null)
            {
                // Zero-Enumeration: exact same response whether nonexistent or belonging to another user
                return Ok(new SrdvBusBookingDetailsResponseDto
                {
                    Success = false,
                    Result = null,
                    TraceId = request.TraceId,
                    Error = new SrdvBusBookingDetailsErrorDto
                    {
                        ErrorCode = 7044,
                        ErrorMessage = "Booking details not found or access denied."
                    }
                });
            }

            var dbPassengers = await dbContext.BusReservationPassengers
                .AsNoTracking()
                .Where(p => p.BusReservationId == booking.Id)
                .OrderBy(p => p.Id)
                .ToListAsync();

            var dbCancellations = await dbContext.BookingCancellations
                .AsNoTracking()
                .Where(c => c.BookingType == "Bus" && c.BookingReference == booking.BookingReference)
                .OrderByDescending(c => c.Id)
                .ToListAsync();

            var blockedSeatPrices = await dbContext.BusBlockedSeatPrices
                .AsNoTracking()
                .Where(b => b.TraceId == normalizedTraceId)
                .ToListAsync();

            var mappedPassengers = dbPassengers.Select((p, idx) =>
            {
                var blockedSeat = blockedSeatPrices.FirstOrDefault(b => b.SeatName.Equals(p.SeatNumber, StringComparison.OrdinalIgnoreCase));

                string pFullName = p.FullName?.Trim() ?? string.Empty;
                string pFirst = !string.IsNullOrWhiteSpace(p.FirstName) ? p.FirstName : (pFullName.Contains(' ') ? pFullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0] : pFullName);
                string pLast = !string.IsNullOrWhiteSpace(p.LastName) ? p.LastName : (pFullName.Contains(' ') ? pFullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[1] : string.Empty);
                string pTitle = p.Title ?? string.Empty;

                decimal baseFare = (p.BaseFareInr > 0)
                    ? p.BaseFareInr
                    : (blockedSeat != null && blockedSeat.BaseFare > 0 ? blockedSeat.BaseFare : 0m);

                decimal publishedFare = (p.PublishedFareInr.HasValue && p.PublishedFareInr.Value > 0)
                    ? p.PublishedFareInr.Value
                    : (blockedSeat != null && blockedSeat.PublishedFare > 0 ? blockedSeat.PublishedFare : baseFare);

                decimal offeredFare = (p.OfferedFareInr.HasValue && p.OfferedFareInr.Value > 0)
                    ? p.OfferedFareInr.Value
                    : (blockedSeat != null && blockedSeat.GrandTotal > 0
                        ? (blockedSeat.GrandTotal - blockedSeat.GstAmount)
                        : (blockedSeat != null && blockedSeat.PublishedFare > 0 ? blockedSeat.PublishedFare - blockedSeat.DiscountAmount : publishedFare));

                decimal gstAmount = p.GstAmountInr.HasValue
                    ? p.GstAmountInr.Value
                    : (blockedSeat != null ? blockedSeat.GstAmount : 0m);

                decimal tax = p.TaxInr.HasValue ? p.TaxInr.Value : gstAmount;

                return new SrdvBusBookingDetailsPassengerDto
                {
                    SeatName = p.SeatNumber,
                    SeatIndex = p.SeatIndex ?? (idx + 1),
                    IsUpper = p.SeatType != null && p.SeatType.Contains("upper", StringComparison.OrdinalIgnoreCase),
                    Title = pTitle,
                    FirstName = pFirst,
                    LastName = pLast,
                    Gender = p.Gender,
                    Age = p.Age,
                    LeadPassenger = p.LeadPassenger,
                    CurrencyCode = "INR",
                    BaseFare = baseFare,
                    Tax = tax,
                    PublishedFare = publishedFare,
                    OfferedFare = offeredFare,
                    GstRate = booking.GstPercent,
                    GSTAmount = gstAmount,
                    CancelStatus = p.IsCancelled ? "Cancelled" : "Active",
                    CancelledAt = p.CancelledAtUtc
                };
            }).ToList();

            var mappedCancellations = dbCancellations.Select(c =>
            {
                List<string> seats = new();
                if (!string.IsNullOrEmpty(c.SeatNamesJson))
                {
                    try { seats = JsonSerializer.Deserialize<List<string>>(c.SeatNamesJson) ?? new(); } catch { }
                }

                string cancelType = !string.IsNullOrWhiteSpace(c.CancellationType)
                    ? c.CancellationType
                    : (seats.Count > 0 && dbPassengers.Count > 0 && seats.Count < dbPassengers.Count ? "PARTIAL" : "FULL");

                string refundStatus = !string.IsNullOrWhiteSpace(c.RefundStatus)
                    ? c.RefundStatus
                    : (c.CustomerRefundAmount > 0 ? "PENDING" : "NOT_REQUIRED");

                return new SrdvBusBookingDetailsCancellationDto
                {
                    CancelId = c.ProviderCancelId,
                    Status = c.Status,
                    CancellationType = cancelType,
                    SeatName = seats,
                    SupplierCancelId = c.SupplierCancelId,
                    RefundAmount = c.CustomerRefundAmount,
                    CancellationCharge = c.SupplierCancellationCharge,
                    RefundStatus = refundStatus,
                    ErrorCode = 0,
                    ErrorMessage = c.FailureReason ?? string.Empty,
                    CompletedAt = c.CompletedAtUtc ?? c.CreatedAtUtc
                };
            }).ToList();

            var cancelStatus = booking.Status == BusBookingStatus.Cancelled ? "Cancelled" : (booking.Status == BusBookingStatus.PartiallyCancelled ? "Partially Cancelled" : "Active");

            var responseDto = new SrdvBusBookingDetailsResponseDto
            {
                Success = true,
                TraceId = request.TraceId,
                Error = new SrdvBusBookingDetailsErrorDto
                {
                    ErrorCode = 0,
                    ErrorMessage = string.Empty
                },
                Result = new SrdvBusBookingDetailsResultDto
                {
                    SrdvIndex = booking.BusBooking?.SrdvIndex ?? 0,
                    ResultIndex = booking.BusBooking?.ResultIndex,
                    BookingId = booking.Id,
                    RefId = booking.BookingReference,
                    BookingStatus = booking.Status,
                    TicketNo = booking.SrdvTicketNo ?? string.Empty,
                    TravelOperatorPNR = booking.Pnr,
                    DsaFare = booking.TotalPriceInr, // MUST represent amount charged to DSA/account
                    CurrencyCode = "INR",
                    CancelStatus = cancelStatus,
                    RefundStatus = booking.FinancialStatus ?? "None",
                    ErrorCode = 0,
                    ErrorMessage = string.Empty,
                    CompletedAt = booking.BookedAtUtc,
                    Passengers = mappedPassengers,
                    Cancellations = mappedCancellations
                }
            };

            return Ok(responseDto);
        }

        [HttpPost("v9/Cancel")]
        [HttpPost("cancel")]
        public async Task<IActionResult> CancelBusTicketV9([FromBody] BusCancelV9RequestDto request)
        {
            if (!currentUserService.IsAuthenticated())
            {
                return Unauthorized("Please login to cancel ticket.");
            }
            var userId = currentUserService.GetUserOrGuestId();

            if (request == null)
            {
                return BadRequest(new { message = "Request body cannot be null." });
            }

            // Reject if BookingId or BusId are provided in request body (contract requirement: use TraceId only)
            if (!string.IsNullOrWhiteSpace(request.BookingId) || !string.IsNullOrWhiteSpace(request.BusId))
            {
                return BadRequest(new
                {
                    message = "Do not pass BookingId or BusId to /v9/Cancel. Use TraceId instead.",
                    Error = new
                    {
                        ErrorCode = 7040,
                        ErrorMessage = "Do not pass BookingId or BusId to /v9/Cancel. Use TraceId instead."
                    }
                });
            }

            if (request.TraceId <= 0)
            {
                return BadRequest(new
                {
                    message = "TraceId must be present, numeric, and greater than 0.",
                    Error = new
                    {
                        ErrorCode = 7041,
                        ErrorMessage = "TraceId must be present, numeric, and greater than 0."
                    }
                });
            }

            // Resolve SeatNames from canonical SeatNames or legacy SeatId alias
            var seatNames = request.SeatNames ?? request.SeatName ?? new List<string>();
            if (seatNames.Count == 0 && !string.IsNullOrWhiteSpace(request.SeatId))
            {
                seatNames = request.SeatId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            if (seatNames.Count == 0)
            {
                return BadRequest(new
                {
                    message = "SeatNames must contain at least one seat to cancel.",
                    Error = new
                    {
                        ErrorCode = 7042,
                        ErrorMessage = "SeatNames must contain at least one seat to cancel."
                    }
                });
            }

            var remarks = !string.IsNullOrWhiteSpace(request.Remarks) 
                ? request.Remarks.Trim() 
                : (!string.IsNullOrWhiteSpace(request.Remark) ? request.Remark.Trim() : "Customer Requested");

            var traceIdStr = request.TraceId.ToString();
            var isAdmin = User?.IsInRole(AuthRoles.Admin) == true;

            var booking = await dbContext.BusReservations
                .Include(x => x.BusBooking)
                .FirstOrDefaultAsync(x => x.BusBooking != null && x.BusBooking.TraceId == traceIdStr && (isAdmin || x.UserId == userId));

            if (booking == null || booking.BusBooking == null)
            {
                return NotFound(new
                {
                    message = "Booking not found or access denied.",
                    Error = new
                    {
                        ErrorCode = 7044,
                        ErrorMessage = "Booking not found or access denied."
                    }
                });
            }

            if (booking.Status == BusBookingStatus.Cancelled || booking.Status == "Cancelled")
            {
                return BadRequest(new
                {
                    message = "Booking is already cancelled.",
                    Error = new
                    {
                        ErrorCode = 7043,
                        ErrorMessage = "Booking is already cancelled."
                    }
                });
            }

            if (booking.BusBooking.DepartureTime <= DateTime.UtcNow)
            {
                return BadRequest(new
                {
                    message = "Cannot cancel ticket after bus departure.",
                    Error = new
                    {
                        ErrorCode = 7046,
                        ErrorMessage = "Cannot cancel ticket after bus departure."
                    }
                });
            }

            // Find passengers matching seat names
            var passengers = await dbContext.BusReservationPassengers
                .Where(x => x.BusReservationId == booking.Id)
                .ToListAsync();

            var targetPassengers = passengers
                .Where(p => !p.IsCancelled && seatNames.Any(s => s.Equals(p.SeatNumber, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (targetPassengers.Count == 0)
            {
                return BadRequest(new
                {
                    message = "Specified seats are already cancelled or do not exist in this booking.",
                    Error = new
                    {
                        ErrorCode = 7047,
                        ErrorMessage = "Specified seats are already cancelled or do not exist in this booking."
                    }
                });
            }

            // Call SRDV CancelTicketV9Async outside DB transaction
            SrdvBusCancelResponseDto? v9Result = null;
            bool isAmbiguousCancel = false;
            try
            {
                v9Result = await _srdvBusService.CancelTicketV9Async(
                    request.TraceId,
                    seatNames,
                    remarks);
            }
            catch (Exception cancelEx)
            {
                isAmbiguousCancel = true;
                logger.LogWarning(cancelEx, "CancelTicketV9Async threw exception for TraceId {TraceId}", request.TraceId);
            }

            // Case A: Explicit supplier rejection
            if (v9Result != null && !v9Result.Success && v9Result.IsExplicitSupplierRejection)
            {
                return BadRequest(new
                {
                    Success = false,
                    message = $"SRDV Cancellation Failed: {v9Result.ErrorMessage}",
                    Error = new
                    {
                        ErrorCode = v9Result.ErrorCode > 0 ? v9Result.ErrorCode : 7048,
                        ErrorMessage = v9Result.ErrorMessage ?? "Cancellation failed by supplier."
                    }
                });
            }

            // Attempt reconciliation via BookingDetails
            decimal srdvCancellationCharge = v9Result?.CancellationCharge ?? 0m;
            decimal srdvRefundAmount = v9Result?.RefundAmount ?? 0m;
            bool cancellationConfirmed = false;

            try
            {
                var details = await _srdvBusService.GetBookingDetailsAsync(traceIdStr);
                var reconciled = TryReconcileCancellationFromDetails(details, seatNames, v9Result?.CancelId);
                if (reconciled.IsConfirmed)
                {
                    cancellationConfirmed = true;
                    srdvCancellationCharge = reconciled.CancellationCharge;
                    srdvRefundAmount = reconciled.RefundAmount;
                }
            }
            catch (Exception dEx)
            {
                logger.LogWarning(dEx, "Details reconciliation failed following V9 cancel for TraceId {TraceId}", traceIdStr);
            }

            var activeSeatNames = passengers
                .Where(x => !x.IsCancelled)
                .Select(x => x.SeatNumber!.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var requestedSeatNames = seatNames
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string cancellationType = requestedSeatNames.SetEquals(activeSeatNames) ? "FULL" : "PARTIAL";

            if (isAmbiguousCancel || (v9Result == null && !cancellationConfirmed))
            {
                // Ambiguous outcome / timeout without confirmation
                var ambAudit = new PickNBook.Api.Models.Entities.BookingCancellation
                {
                    BookingType = "Bus",
                    BookingReference = booking.BookingReference,
                    UserId = booking.UserId,
                    CreatedAtUtc = DateTime.UtcNow,
                    OriginalCustomerPaid = 0m,
                    SupplierAmount = 0m,
                    MarkupAmount = 0m,
                    ConvenienceFee = 0m,
                    DiscountAmount = 0m,
                    SupplierRefundAmount = 0m,
                    SupplierCancellationCharge = 0m,
                    CustomerRefundAmount = 0m,
                    Status = "MANUAL_CHECK_REQUIRED",
                    CancellationType = cancellationType,
                    RefundStatus = "MANUAL_CHECK_REQUIRED",
                    TraceId = request.TraceId,
                    SeatNamesJson = JsonSerializer.Serialize(seatNames),
                    FailureReason = "Cancellation timed out or produced ambiguous response; awaiting manual review."
                };
                dbContext.BookingCancellations.Add(ambAudit);
                await dbContext.SaveChangesAsync();

                return BadRequest(new
                {
                    Success = false,
                    message = "MANUAL_CHECK_REQUIRED: Cancellation outcome is ambiguous and awaiting manual reconciliation.",
                    Error = new
                    {
                        ErrorCode = 7049,
                        ErrorMessage = "MANUAL_CHECK_REQUIRED: Cancellation outcome is ambiguous and awaiting manual reconciliation."
                    }
                });
            }

            long cancelId = v9Result?.CancelId ?? 0L;
            string? supplierCancelId = v9Result?.SupplierCancelId;

            // Only when cancellation is authoritatively confirmed do passengers become cancelled!
            if (cancellationConfirmed)
            {
                foreach (var p in targetPassengers)
                {
                    p.IsCancelled = true;
                    p.CancelledAtUtc = DateTime.UtcNow;
                }

                var allActiveCancelled = passengers.All(p => p.IsCancelled);
                if (allActiveCancelled)
                {
                    booking.Status = BusBookingStatus.Cancelled;
                    booking.CancelledAtUtc = DateTime.UtcNow;
                    booking.CancellationReason = remarks;
                }
                else
                {
                    booking.Status = BusBookingStatus.PartiallyCancelled;
                }
            }
            else
            {
                // Unconfirmed In Process: remains active, status set to CANCEL_IN_PROCESS
                booking.Status = BusBookingStatus.CancelInProcess;
                // Passengers remain active (IsCancelled = false)
            }

            booking.ProviderCancelId = cancelId > 0 ? cancelId : null;
            booking.SupplierCancelId = supplierCancelId;

            // Refund calculation
            var totalSeats = passengers.Count > 0 ? passengers.Count : 1;
            decimal proportion = (decimal)targetPassengers.Count / totalSeats;

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
            var calculatedRefund = refundCalculator.CalculateCustomerRefund(refundInput);

            if (cancellationConfirmed)
            {
                booking.CancellationChargeInr = (booking.CancellationChargeInr ?? 0m) + calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                booking.RefundAmountInr = (booking.RefundAmountInr ?? 0m) + calculatedRefund.FinalCustomerRefundAmount;
                booking.FinancialStatus = calculatedRefund.FinalCustomerRefundAmount > 0 ? "PENDING_REFUND" : "NO_REFUND";
            }

            var auditStatus = cancellationConfirmed ? "PendingReview" : "CANCEL_IN_PROCESS";
            string refundAuditStatus = cancellationConfirmed
                ? (calculatedRefund.FinalCustomerRefundAmount > 0 ? "PENDING" : "NOT_REQUIRED")
                : "PENDING";

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
                CustomerRefundAmount = cancellationConfirmed ? calculatedRefund.FinalCustomerRefundAmount : 0m,
                Status = auditStatus, // PendingReview for Admin reconciliation, or CANCEL_IN_PROCESS
                CancellationType = cancellationType,
                RefundStatus = refundAuditStatus,
                TraceId = request.TraceId,
                ProviderCancelId = cancelId > 0 ? cancelId : null,
                SupplierCancelId = supplierCancelId,
                SeatNamesJson = JsonSerializer.Serialize(seatNames)
            };
            dbContext.BookingCancellations.Add(cancellationAudit);

            await dbContext.SaveChangesAsync();

            return Ok(new
            {
                Success = true,
                CancelId = cancelId,
                SupplierCancelId = supplierCancelId,
                CancellationCharge = srdvCancellationCharge,
                RefundAmount = cancellationConfirmed ? calculatedRefund.FinalCustomerRefundAmount : 0m,
                Status = booking.Status,
                FinancialStatus = booking.FinancialStatus,
                Remarks = remarks,
                Booking = MapBusReservation(booking, booking.BusBooking, passengers)
            });
        }
    }

}
