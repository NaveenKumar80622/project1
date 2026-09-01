using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using PickNBook.Api.Data;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers.Public
{
    [Route("api/flight/srdv")]
    [ApiController]
    public class SrdvFlightApiController : ControllerBase
    {
        private readonly ISrdvFlightService _srdvFlightService;
        private readonly IFlightPricingService _pricingService;
        private readonly AppDbContext _dbContext;
        private readonly ITicketEmailService _ticketEmailService;
        private readonly IAgentWalletService _walletService;
        private readonly SrdvSettings _srdvSettings;
        private readonly ILogger<SrdvFlightApiController> _logger;
        private readonly ICancellationRefundCalculator _refundCalculator;

        public SrdvFlightApiController(
            ISrdvFlightService srdvFlightService, 
            IFlightPricingService pricingService,
            AppDbContext dbContext,
            ITicketEmailService ticketEmailService,
            IAgentWalletService walletService,
            IOptions<SrdvSettings> srdvSettings,
            ICancellationRefundCalculator refundCalculator,
            ILogger<SrdvFlightApiController> logger)
        {
            _srdvFlightService = srdvFlightService;
            _pricingService = pricingService;
            _dbContext = dbContext;
            _ticketEmailService = ticketEmailService;
            _walletService = walletService;
            _srdvSettings = srdvSettings.Value;
            _refundCalculator = refundCalculator;
            _logger = logger;
        }

        [HttpPost("Search")]
        public async Task<IActionResult> Search([FromBody] FlightSearchProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new AirSearchRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    AdultCount = proxyRequest.AdultCount,
                    ChildCount = proxyRequest.ChildCount,
                    InfantCount = proxyRequest.InfantCount,
                    JourneyType = proxyRequest.JourneyType,
                    DirectFlight = proxyRequest.DirectFlight,
                    Segments = proxyRequest.Segments
                };
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
                
                var responseRaw = await _srdvFlightService.SearchFlightsRawAsync(request);
                var jsonNode = JsonNode.Parse(responseRaw);
                var responseObj = jsonNode; // The root is the response object
                
                var errorCode = responseObj?["Error"]?["ErrorCode"]?.ToString();
                
                var requestTripType = request.JourneyType == 2 ? TripType.RoundTrip : (request.JourneyType == 3 ? TripType.MultiCity : TripType.OneWay);
                
                _logger.LogInformation("Search Flight API triggered. SRDV ErrorCode: {ErrorCode}", errorCode);
                
                if (errorCode == "0") // SRDV V8: ErrorCode 0 = success, any other value = error
                {
                    try 
                    {
                        var traceId = responseObj?["TraceId"]?.ToString();
                        
                        var searchLog = new FlightSearchLog
                        {
                            SearchedAtUtc = DateTime.UtcNow,
                            FromCity = request.Segments.FirstOrDefault()?.Origin ?? "",
                            ToCity = request.Segments.LastOrDefault()?.Destination ?? "",
                            DepartDate = request.Segments.FirstOrDefault() != null ? DateOnly.FromDateTime(request.Segments.First().PreferredDepartureTime) : null,
                            ReturnDate = request.Segments.Count > 1 ? DateOnly.FromDateTime(request.Segments.Last().PreferredDepartureTime) : null,
                            Adults = request.AdultCount,
                            Children = request.ChildCount,
                            Infants = request.InfantCount,
                            TripType = request.JourneyType.ToString(),
                            UserId = string.IsNullOrEmpty(userId) ? null : userId,
                            IsGuest = string.IsNullOrEmpty(userId),
                            UserOrGuestId = userId,
                            TraceId = traceId,
                            EndUserIp = request.EndUserIp
                        };
                        
                        _dbContext.FlightSearchLogs.Add(searchLog);
                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation("Successfully inserted flight search log to Database. TraceId: {TraceId}", traceId);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "FATAL: Failed to insert flight search log to database!");
                        // Don't fail the whole request just because logging failed
                    }
                }
                else
                {
                    _logger.LogWarning("ErrorCode was not 0. It was {ErrorCode}. Not logging to DB.", errorCode);
                    if (responseObj?["Error"] is System.Text.Json.Nodes.JsonObject errObj)
                    {
                        errObj["ErrorMessage"] = PickNBook.Api.Infrastructure.Helpers.SrdvErrorHelper.GetErrorMessage(errorCode);
                    }
                }

                var resultsArr = responseObj?["Results"]?.AsArray();
                    if (resultsArr != null)
                    {
                        foreach (var flightList in resultsArr)
                        {
                            var flightsArr = flightList?.AsArray();
                            if (flightsArr != null)
                            {
                                foreach (var result in flightsArr)
                                {
                                    if (result == null) continue;
                                    
                                    var segmentsArr = result["Segments"]?[0]?.AsArray();
                                    if (segmentsArr == null || segmentsArr.Count == 0) continue;
                                    
                                    var firstSegment = segmentsArr[0];
                                    var lastSegment = segmentsArr[segmentsArr.Count - 1];
                                    
                                    var airlineCode = firstSegment?["Airline"]?["AirlineCode"]?.GetValue<string>() ?? "";
                                    var airlineName = firstSegment?["Airline"]?["AirlineName"]?.GetValue<string>() ?? "";
                                    var origin = firstSegment?["Origin"]?["Airport"]?["CityCode"]?.GetValue<string>() ?? "";
                                    var destination = lastSegment?["Destination"]?["Airport"]?["CityCode"]?.GetValue<string>() ?? "";
                                    
                                    var depTimeNode = firstSegment?["DepTime"] ?? firstSegment?["Origin"]?["DepTime"];
                                    var depTime = depTimeNode?.GetValue<DateTime>() ?? DateTime.UtcNow;
                                    
                                    var travelClassStr = firstSegment?["CabinClass"]?.GetValue<int>() switch
                                    {
                                        2 => "Economy",
                                        3 => "PremiumEconomy",
                                        4 => "Business",
                                        5 => "PremiumBusiness",
                                        6 => "First",
                                        _ => "Economy"
                                    };

                                    var fareDataMultipleArr = result["FareDataMultiple"]?.AsArray();
                                    if (fareDataMultipleArr != null && fareDataMultipleArr.Count > 0)
                                    {
                                        decimal? firstFinalAmount = null;
                                        foreach (var fareData in fareDataMultipleArr)
                                        {
                                            if (fareData == null) continue;
                                            var fObj = fareData["Fare"];
                                            if (fObj != null)
                                            {
                                                var bf = fObj["BaseFare"]?.GetValue<decimal>() ?? 0m;
                                                var tx = fObj["Tax"]?.GetValue<decimal>() ?? 0m;
                                                
                                                var breakdown = await _pricingService.CalculatePricingAsync(
                                                    supplierBaseFare: bf,
                                                    supplierTaxAmount: tx,
                                                    airlineCode: airlineCode,
                                                    airlineName: airlineName,
                                                    origin: origin,
                                                    destination: destination,
                                                    departureDate: depTime,
                                                    travelClass: travelClassStr,
                                                    tripType: requestTripType,
                                                    passengerCount: request.AdultCount + request.ChildCount + request.InfantCount,
                                                    couponCode: null,
                                                    userId: userId
                                                );
                                                
                                                fObj["B2CFinalFare"] = breakdown.FinalAmount;
                                                fObj["B2CPublishedFare"] = breakdown.SupplierTotalFare + breakdown.MarkupAmount;
                                                fObj["B2CMarkupAmount"] = breakdown.MarkupAmount;
                                                if (fareData["OfferedFare"] != null) 
                                                {
                                                    fareData["B2CFinalFare"] = breakdown.FinalAmount;
                                                    fareData["B2CPublishedFare"] = breakdown.SupplierTotalFare + breakdown.MarkupAmount;
                                                    fareData["B2CMarkupAmount"] = breakdown.MarkupAmount;
                                                }
                                                
                                                fareData["PickNBookMarkup"] = breakdown.MarkupAmount;
                                                fareData["PickNBookDiscount"] = breakdown.PromotionDiscount + breakdown.CouponDiscount;

                                                if (firstFinalAmount == null)
                                                    firstFinalAmount = breakdown.FinalAmount;
                                            }
                                        }
                                        if (firstFinalAmount != null && result["OfferedFare"] != null)
                                        {
                                            result["B2CFinalFare"] = firstFinalAmount;
                                        }
                                    }
                                    else
                                    {
                                        var fareObj = result["Fare"];
                                        var baseFare = fareObj?["BaseFare"]?.GetValue<decimal>() ?? 0m;
                                        var tax = fareObj?["Tax"]?.GetValue<decimal>() ?? 0m;
                                        
                                        var pricingBreakdown = await _pricingService.CalculatePricingAsync(
                                            supplierBaseFare: baseFare,
                                            supplierTaxAmount: tax,
                                            airlineCode: airlineCode,
                                            airlineName: airlineName,
                                            origin: origin,
                                            destination: destination,
                                            departureDate: depTime,
                                            travelClass: travelClassStr,
                                            tripType: requestTripType,
                                            passengerCount: request.AdultCount + request.ChildCount + request.InfantCount,
                                            couponCode: null,
                                            userId: userId
                                        );
                                        
                                        if (fareObj != null)
                                        {
                                            fareObj["B2CFinalFare"] = pricingBreakdown.FinalAmount;
                                            fareObj["B2CPublishedFare"] = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount;
                                            fareObj["B2CMarkupAmount"] = pricingBreakdown.MarkupAmount;
                                        }
                                        if (result["OfferedFare"] != null) 
                                        {
                                            result["B2CFinalFare"] = pricingBreakdown.FinalAmount;
                                            result["B2CPublishedFare"] = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount;
                                            result["B2CMarkupAmount"] = pricingBreakdown.MarkupAmount;
                                        }
                                        
                                        result["PickNBookMarkup"] = pricingBreakdown.MarkupAmount;
                                        result["PickNBookDiscount"] = pricingBreakdown.PromotionDiscount + pricingBreakdown.CouponDiscount;
                                    }
                                }
                            }
                        }
                    }
                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching flights.");
                return StatusCode(500, new { message = "Failed to search flights.", error = ex.Message });
            }
        }

        [HttpPost("GetCalendarFare")]
        public async Task<IActionResult> GetCalendarFare([FromBody] FlightCalendarFareProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new CalendarFareRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    JourneyType = proxyRequest.JourneyType,
                    Sources = proxyRequest.Sources,
                    FareType = proxyRequest.FareType,
                    Segments = proxyRequest.Segments
                };
                var responseRaw = await _srdvFlightService.GetCalendarFareRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Calendar Fare.");
                return StatusCode(500, new { message = "Failed to get Calendar Fare.", error = ex.Message });
            }
        }

        [HttpPost("FareRule")]
        public async Task<IActionResult> FareRule([FromBody] FlightFareRuleProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new AirFareRuleRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType,
                    SrdvIndex = proxyRequest.SrdvIndex,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex,
                    CouponCode = proxyRequest.CouponCode,
                    JourneyType = proxyRequest.JourneyType,
                    AdultCount = proxyRequest.AdultCount,
                    ChildCount = proxyRequest.ChildCount,
                    InfantCount = proxyRequest.InfantCount
                };
                var responseRaw = await _srdvFlightService.GetFareRuleRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting fare rule.");
                return StatusCode(500, new { message = "Failed to get fare rule.", error = ex.Message });
            }
        }

        [HttpPost("FareQuote")]
        public async Task<IActionResult> FareQuote([FromBody] FlightFareRuleProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new AirFareRuleRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType,
                    SrdvIndex = proxyRequest.SrdvIndex,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex,
                    CouponCode = proxyRequest.CouponCode,
                    JourneyType = proxyRequest.JourneyType,
                    AdultCount = proxyRequest.AdultCount,
                    ChildCount = proxyRequest.ChildCount,
                    InfantCount = proxyRequest.InfantCount
                };
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
                
                var responseRaw = await _srdvFlightService.GetFareQuoteRawAsync(request);
                var jsonNode = JsonNode.Parse(responseRaw);
                
                // Support both TBO (wrapped in "Response") and MixAPI (flat root)
                var responseObj = jsonNode?["Response"] ?? jsonNode;
                
                // MixAPI doesn't have ResponseStatus, it uses Error.ErrorCode
                var errorCode = responseObj?["Error"]?["ErrorCode"]?.ToString();
                var isSuccess = responseObj?["ResponseStatus"]?.GetValue<int>() == 1 || 
                                (errorCode == "0" || errorCode == null);

                if (isSuccess)
                {
                    var result = responseObj?["Results"];
                    if (result != null)
                    {
                        var fareObj = result["Fare"];
                        var baseFare = fareObj?["BaseFare"]?.GetValue<decimal>() ?? 0m;
                        var tax = fareObj?["Tax"]?.GetValue<decimal>() ?? 0m;
                        
                        var segmentsArr = result["Segments"]?[0]?.AsArray();
                        if (segmentsArr != null && segmentsArr.Count > 0)
                        {
                            var firstSegment = segmentsArr[0];
                            var lastSegment = segmentsArr[segmentsArr.Count - 1];
                            
                            var airlineCode = firstSegment?["Airline"]?["AirlineCode"]?.GetValue<string>() ?? "";
                            var airlineName = firstSegment?["Airline"]?["AirlineName"]?.GetValue<string>() ?? "";
                            var origin = firstSegment?["Origin"]?["Airport"]?["CityCode"]?.GetValue<string>() ?? "";
                            var destination = lastSegment?["Destination"]?["Airport"]?["CityCode"]?.GetValue<string>() ?? "";
                            var depTime = firstSegment?["Origin"]?["DepTime"]?.GetValue<DateTime>() ?? DateTime.UtcNow;
                            var travelClassStr = firstSegment?["CabinClass"]?.GetValue<int>() switch
                            {
                                2 => "Economy",
                                3 => "PremiumEconomy",
                                4 => "Business",
                                5 => "PremiumBusiness",
                                6 => "First",
                                _ => "Economy"
                            };

                                TripType fqTripType = TripType.OneWay;
                                if (request.JourneyType.HasValue)
                                {
                                    fqTripType = request.JourneyType == 2 ? TripType.RoundTrip : (request.JourneyType == 3 ? TripType.MultiCity : TripType.OneWay);
                                }
                                else if (!string.IsNullOrEmpty(request.ResultIndex) && request.ResultIndex.Contains(","))
                                {
                                    fqTripType = TripType.RoundTrip;
                                }
                                var fqPaxCount = (request.AdultCount ?? 1) + (request.ChildCount ?? 0) + (request.InfantCount ?? 0);

                                var pricingBreakdown = await _pricingService.CalculatePricingAsync(
                                    supplierBaseFare: baseFare,
                                    supplierTaxAmount: tax,
                                    airlineCode: airlineCode,
                                    airlineName: airlineName,
                                    origin: origin,
                                    destination: destination,
                                    departureDate: depTime,
                                    travelClass: travelClassStr,
                                    tripType: fqTripType,
                                    passengerCount: fqPaxCount,
                                    couponCode: request.CouponCode,
                                    userId: userId
                                );
                            
                            result["B2CFinalFare"] = pricingBreakdown.FinalAmount;
                            result["B2CPublishedFare"] = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount;
                            result["B2CMarkupAmount"] = pricingBreakdown.MarkupAmount;
                            
                            var displayBaseFare = baseFare + pricingBreakdown.MarkupAmount;
                            var displayTax = pricingBreakdown.FinalAmount - displayBaseFare;
                            result["DisplayBaseFare"] = displayBaseFare;
                            result["DisplayTax"] = displayTax;
                            
                            result["PickNBookMarkup"] = pricingBreakdown.MarkupAmount;
                            result["PickNBookDiscount"] = pricingBreakdown.PromotionDiscount + pricingBreakdown.CouponDiscount;

                            var activeOffers = await _dbContext.FeaturedOffers
                                .Where(f => f.IsActive && f.BookingType.ToLower() == "flight")
                                .Select(f => new { f.Title, f.Description, f.DiscountType, f.DiscountValue, Code = f.Title })
                                .ToListAsync();
                            result["PickNBookAvailableOffers"] = JsonSerializer.SerializeToNode(activeOffers);
                        }
                    }
                }
                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting fare quote.");
                return StatusCode(500, new { message = "Failed to get fare quote.", error = ex.Message });
            }
        }

        [HttpPost("SSR")]
        public async Task<IActionResult> SSR([FromBody] FlightFareRuleProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new AirFareRuleRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType,
                    SrdvIndex = proxyRequest.SrdvIndex,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex,
                    CouponCode = proxyRequest.CouponCode,
                    JourneyType = proxyRequest.JourneyType,
                    AdultCount = proxyRequest.AdultCount,
                    ChildCount = proxyRequest.ChildCount,
                    InfantCount = proxyRequest.InfantCount
                };
                var responseRaw = await _srdvFlightService.GetSSRRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SSR.");
                return StatusCode(500, new { message = "Failed to get SSR.", error = ex.Message });
            }
        }

        [HttpPost("SeatMap")]
        public async Task<IActionResult> SeatMap([FromBody] FlightFareRuleProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new AirFareRuleRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType,
                    SrdvIndex = proxyRequest.SrdvIndex,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex,
                    CouponCode = proxyRequest.CouponCode,
                    JourneyType = proxyRequest.JourneyType,
                    AdultCount = proxyRequest.AdultCount,
                    ChildCount = proxyRequest.ChildCount,
                    InfantCount = proxyRequest.InfantCount
                };
                var responseRaw = await _srdvFlightService.GetSeatMapRawAsync(request);
                var outNode = JsonNode.Parse(responseRaw);
                if (outNode != null) InjectSeatMapB2CFields(outNode);
                return Ok(outNode ?? (object)JsonDocument.Parse(responseRaw).RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting seat map.");
                return StatusCode(500, new { message = "Failed to get seat map.", error = ex.Message });
            }
        }

        private void InjectSeatMapB2CFields(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                if (obj.ContainsKey("Price"))
                {
                    obj["B2CFinalFare"] = obj["Price"]?.DeepClone();
                    obj["B2CMarkupAmount"] = 0;
                }
                foreach (var kvp in obj.ToList())
                {
                    if (kvp.Value != null)
                        InjectSeatMapB2CFields(kvp.Value);
                }
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item != null)
                        InjectSeatMapB2CFields(item);
                }
            }
        }


        [Authorize]
        [HttpPost("TicketLCC")]
        public async Task<IActionResult> TicketLCC([FromBody] FlightTicketLCCProxyRequestDto proxyRequest)
        {
            var request = new TicketLCCRequestDto
            {
                EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                SrdvType = proxyRequest.SrdvType,
                SrdvIndex = proxyRequest.SrdvIndex,
                TraceId = proxyRequest.TraceId,
                ResultIndex = proxyRequest.ResultIndex,
                CouponCode = proxyRequest.CouponCode,
                PromoCode = proxyRequest.PromoCode,
                PromotionId = proxyRequest.PromotionId,
                JourneyType = proxyRequest.JourneyType,
                Passengers = proxyRequest.Passengers
            };

            var passportValidationResult = ValidatePassengersPassport(request.Passengers);
            if (passportValidationResult != null) return passportValidationResult;

            try
            {
                var responseRaw = await _srdvFlightService.TicketLCCRawAsync(request);
                var outNode = JsonNode.Parse(responseRaw);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;
                
                bool isSuccess = false;
                bool isPending = false;
                
                if (root.TryGetProperty("ResponseStatus", out var status))
                {
                    if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                    if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                }
                
                if (root.TryGetProperty("Error", out var err) && err.TryGetProperty("ErrorCode", out var errCode))
                {
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;

                    // ErrorCode 10 = Pending (booking in process)
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 10) isPending = true;
                    if (errCode.ValueKind == JsonValueKind.String && errCode.ToString() == "10") isPending = true;
                }

                JsonElement resp = root;
                if (root.TryGetProperty("Response", out var responseNode))
                {
                    resp = responseNode;
                }

                // Also detect pending from TicketStatus field
                if (resp.TryGetProperty("TicketStatus", out var tStatus) && tStatus.ToString()?.Equals("Pending", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isPending = true;
                }

                bool isPriceChanged = resp.TryGetProperty("IsPriceChanged", out var ipc) && ipc.ValueKind == JsonValueKind.True;
                int ticketStatusCode = resp.TryGetProperty("TicketStatus", out var tsCode) && tsCode.ValueKind == JsonValueKind.Number ? tsCode.GetInt32() : -1;
                bool ssrDenied = resp.TryGetProperty("SSRDenied", out var ssrDenNode) && ssrDenNode.ValueKind == JsonValueKind.True;
                string? ssrMessage = resp.TryGetProperty("SSRMessage", out var ssrMsgNode) && ssrMsgNode.ValueKind == JsonValueKind.String ? ssrMsgNode.GetString() : null;

                string pnr = resp.TryGetProperty("PNR", out var pnrProp) ? (pnrProp.ToString() ?? "") : "";
                string bookingId = resp.TryGetProperty("BookingId", out var bIdProp) ? (bIdProp.ToString() ?? "") : "";

                if ((isSuccess || isPending) && (!string.IsNullOrEmpty(pnr) || !string.IsNullOrEmpty(bookingId)))
                {
                    decimal totalFare = 0, baseFare = 0, tax = 0, netFare = 0, customerFare = 0, ssrFromResponse = 0m;
                    string airline = "", flightNumber = "", fromCity = "", toCity = "";
                    DateTime depTime = DateTime.MinValue, arrTime = DateTime.MinValue;
                    bool nonRefundable = false;
                    string segmentsJson = "", fareRulesJson = "", travelClassStr = "Economy";
                    string cancellationCharges = "";
                    string partialSegmentCancellation = "";

                    if (resp.TryGetProperty("FlightItinerary", out var itinerary))
                    {
                        nonRefundable = itinerary.TryGetProperty("NonRefundable", out var isRef) && isRef.ValueKind == JsonValueKind.True;

                        if (itinerary.TryGetProperty("FareRules", out var fr))
                            fareRulesJson = fr.ToString();
                        else if (itinerary.TryGetProperty("MiniFareRules", out var mfr))
                            fareRulesJson = mfr.ToString();
                            
                        if (itinerary.TryGetProperty("CancellationCharges", out var cancNode))
                            cancellationCharges = cancNode.ToString();
                        if (itinerary.TryGetProperty("PartialSegmentCancellation", out var pscNode))
                            partialSegmentCancellation = pscNode.ToString();

                        if (itinerary.TryGetProperty("Fare", out var fare))
                        {
                            totalFare = fare.TryGetProperty("PublishedFare", out var pubFare) && pubFare.ValueKind == JsonValueKind.Number ? pubFare.GetDecimal() : 0;
                            baseFare = fare.TryGetProperty("BaseFare", out var bFare) && bFare.ValueKind == JsonValueKind.Number ? bFare.GetDecimal() : 0;
                            tax = fare.TryGetProperty("Tax", out var tFare) && tFare.ValueKind == JsonValueKind.Number ? tFare.GetDecimal() : 0;
                            customerFare = totalFare;
                            netFare = fare.TryGetProperty("OfferedFare", out var offFare) && offFare.ValueKind == JsonValueKind.Number ? offFare.GetDecimal() : totalFare;

                            decimal totalBaggage = 0, totalMeal = 0, totalSeat = 0, totalSsr = 0;
                            if (fare.TryGetProperty("TotalBaggageCharges", out var bagNode))
                            {
                                if (bagNode.ValueKind == JsonValueKind.Number) totalBaggage = bagNode.GetDecimal();
                                else if (bagNode.ValueKind == JsonValueKind.String && decimal.TryParse(bagNode.GetString(), out var parsedBag)) totalBaggage = parsedBag;
                            }
                            if (fare.TryGetProperty("TotalMealCharges", out var mealNode))
                            {
                                if (mealNode.ValueKind == JsonValueKind.Number) totalMeal = mealNode.GetDecimal();
                                else if (mealNode.ValueKind == JsonValueKind.String && decimal.TryParse(mealNode.GetString(), out var parsedMeal)) totalMeal = parsedMeal;
                            }
                            if (fare.TryGetProperty("TotalSeatCharges", out var seatNode))
                            {
                                if (seatNode.ValueKind == JsonValueKind.Number) totalSeat = seatNode.GetDecimal();
                                else if (seatNode.ValueKind == JsonValueKind.String && decimal.TryParse(seatNode.GetString(), out var parsedSeat)) totalSeat = parsedSeat;
                            }
                            if (fare.TryGetProperty("TotalSpecialServiceCharges", out var ssrNode))
                            {
                                if (ssrNode.ValueKind == JsonValueKind.Number) totalSsr = ssrNode.GetDecimal();
                                else if (ssrNode.ValueKind == JsonValueKind.String && decimal.TryParse(ssrNode.GetString(), out var parsedSsr)) totalSsr = parsedSsr;
                            }
                            ssrFromResponse = totalBaggage + totalMeal + totalSeat + totalSsr;
                        }

                        if (itinerary.TryGetProperty("Segments", out var segs) && segs.ValueKind == JsonValueKind.Array && segs.GetArrayLength() > 0)
                        {
                            segmentsJson = segs.ToString();
                            var firstSeg = segs[0];
                            if (firstSeg.TryGetProperty("Airline", out var alNode))
                            {
                                airline = alNode.TryGetProperty("AirlineName", out var alNameNode) ? (alNameNode.ToString() ?? "") : "";
                                flightNumber = alNode.TryGetProperty("FlightNumber", out var fnNode) ? (fnNode.ToString() ?? "") : "";
                            }
                            
                            if (firstSeg.TryGetProperty("Origin", out var orig) && orig.TryGetProperty("CityCode", out var origCity))
                                fromCity = origCity.ToString() ?? "";
                            if (firstSeg.TryGetProperty("Destination", out var dest) && dest.TryGetProperty("CityCode", out var destCity))
                                toCity = destCity.ToString() ?? "";
                            
                            if (firstSeg.TryGetProperty("DepTime", out var dTime) && DateTime.TryParse(dTime.ToString(), out var parsedDep))
                                depTime = parsedDep;

                            var lastSeg = segs[segs.GetArrayLength() - 1];
                            if (lastSeg.TryGetProperty("Destination", out var dest2) && dest2.TryGetProperty("CityCode", out var destCity2))
                                toCity = destCity2.ToString() ?? toCity;
                            if (lastSeg.TryGetProperty("ArrTime", out var aTime) && DateTime.TryParse(aTime.ToString(), out var parsedArr))
                                arrTime = parsedArr;
                            if (firstSeg.TryGetProperty("CabinClass", out var cClass) && cClass.ValueKind == JsonValueKind.Number)
                            {
                                travelClassStr = cClass.GetInt32() switch
                                {
                                    2 => "Economy",
                                    3 => "PremiumEconomy",
                                    4 => "Business",
                                    5 => "PremiumBusiness",
                                    6 => "First",
                                    _ => "Economy"
                                };
                            }
                        }
                    }
                    var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                    
                    var firstPax = request.Passengers?.FirstOrDefault();
                    string paxName = firstPax != null ? $"{firstPax.FirstName} {firstPax.LastName}" : "";
                    string paxPhone = firstPax != null ? firstPax.ContactNo : "";
                    string paxEmail = firstPax != null ? firstPax.Email : "";
                    int adults = request.Passengers?.Count(p => p.PaxType == 1) ?? 0;
                    int children = request.Passengers?.Count(p => p.PaxType == 2) ?? 0;
                    int infants = request.Passengers?.Count(p => p.PaxType == 3) ?? 0;
                    int seatsBooked = adults + children;

                    TripType parsedTripType = TripType.OneWay;
                    if (request.JourneyType.HasValue)
                    {
                        parsedTripType = request.JourneyType == 2 ? TripType.RoundTrip : (request.JourneyType == 3 ? TripType.MultiCity : TripType.OneWay);
                    }
                    else if (!string.IsNullOrEmpty(request.ResultIndex) && request.ResultIndex.Contains(","))
                    {
                        parsedTripType = TripType.RoundTrip;
                    }
                    else
                    {
                        parsedTripType = (resp.TryGetProperty("FlightItinerary", out var lccIt) && lccIt.TryGetProperty("Segments", out var lccSegs) && lccSegs.ValueKind == JsonValueKind.Array && lccSegs.GetArrayLength() > 1) ? TripType.RoundTrip : TripType.OneWay;
                    }

                    var pricingBreakdown = await _pricingService.CalculatePricingAsync(
                        supplierBaseFare: baseFare,
                        supplierTaxAmount: tax,
                        airlineCode: airline,
                        airlineName: airline,
                        origin: fromCity,
                        destination: toCity,
                        departureDate: depTime,
                        travelClass: travelClassStr,
                        tripType: parsedTripType,
                        passengerCount: adults + children + infants,
                        couponCode: request.CouponCode,
                        userId: userIdStr,
                        selectedPromotionId: request.PromotionId
                    );

                    if (outNode != null)
                    {
                        var respObj = outNode["Response"] ?? outNode;
                        var fareNode = respObj["FlightItinerary"]?["Fare"];
                        if (fareNode != null)
                        {
                            fareNode["B2CFinalFare"] = pricingBreakdown.FinalAmount + ssrFromResponse;
                            fareNode["B2CPublishedFare"] = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount + ssrFromResponse;
                            fareNode["B2CMarkupAmount"] = pricingBreakdown.MarkupAmount;
                        }
                    }

                    var reservation = new FlightReservation
                    {
                        BookingReference = $"FL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                        Pnr = pnr,
                        UserId = userIdStr,
                        Status = isPending ? "Pending" : "Booked",
                        BookedAtUtc = DateTime.UtcNow,
                        SSRDenied = ssrDenied,
                        SSRMessage = ssrMessage,
                        
                        TraceId = resp.TryGetProperty("TraceId", out var newTraceId) && newTraceId.ValueKind == JsonValueKind.String ? newTraceId.GetString() ?? request.TraceId : request.TraceId,
                        ResultIndex = request.ResultIndex,
                        FlightNumber = flightNumber,
                        Airline = airline,
                        FromCity = fromCity,
                        ToCity = toCity,
                        DepartureTime = depTime,
                        ArrivalTime = arrTime,
                        SegmentsJson = segmentsJson,
                        
                        NonRefundable = nonRefundable,
                        FareRulesJson = fareRulesJson,

                        TotalPriceInr = pricingBreakdown.FinalAmount + ssrFromResponse,
                        CustomerFareInr = pricingBreakdown.FinalAmount + ssrFromResponse,
                        NetFareInr = netFare,
                        SupplierBaseFare = baseFare,
                        SupplierTaxAmount = tax,
                        SupplierTotalFare = totalFare,
                        SsrAmountInr = ssrFromResponse,
                        MarkupAmount = pricingBreakdown.MarkupAmount,
                        B2CPublishedFareInr = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount,
                        B2CMarkupAmountInr = pricingBreakdown.MarkupAmount,
                        B2CDiscountAmountInr = pricingBreakdown.PromotionDiscount + pricingBreakdown.CouponDiscount,
                        PromotionDiscount = pricingBreakdown.PromotionDiscount,
                        CouponDiscount = pricingBreakdown.CouponDiscount,
                        SrdvTicketResponseJson = responseRaw,
                        
                        PassengerName = paxName,
                        PassengerPhone = paxPhone,
                        PassengerEmail = paxEmail,
                        Adults = adults,
                        Children = children,
                        Infants = infants,
                        SeatsBooked = seatsBooked,
                        
                        SrdvBookingId = bookingId,
                        SrdvPnr = pnr,
                        TicketStatus = resp.TryGetProperty("TicketStatus", out var ts) ? ts.ToString() : null,
                        IsLcc = true,
                        SrdvType = request.SrdvType,
                        SrdvIndex = request.SrdvIndex,
                        ReturnPnr = resp.TryGetProperty("ReturnPNR", out var rpNode) ? rpNode.ToString() : null
                    };

                    if (!string.IsNullOrEmpty(segmentsJson))
                    {
                        try
                        {
                            var parsedSegments = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(segmentsJson);
                            if (parsedSegments != null)
                            {
                                foreach (var seg in parsedSegments)
                                {
                                    var segObj = new PickNBook.Api.Models.FlightReservationSegment
                                    {
                                        TripIndicator = seg.TryGetProperty("TripIndicator", out var ti) && ti.ValueKind == System.Text.Json.JsonValueKind.Number ? ti.GetInt32() : 0,
                                        SegmentIndicator = seg.TryGetProperty("SegmentIndicator", out var si) && si.ValueKind == System.Text.Json.JsonValueKind.Number ? si.GetInt32() : 0,
                                        Baggage = seg.TryGetProperty("Baggage", out var bag) ? bag.ToString() : null,
                                        CabinBaggage = seg.TryGetProperty("CabinBaggage", out var cBag) ? cBag.ToString() : null,
                                        Duration = seg.TryGetProperty("Duration", out var dur) && dur.ValueKind == System.Text.Json.JsonValueKind.Number ? dur.GetInt32() : 0,
                                        Airline = seg.TryGetProperty("Airline", out var al) && al.TryGetProperty("AirlineName", out var aln) ? aln.ToString() ?? "" : "",
                                        FlightNumber = seg.TryGetProperty("Airline", out var al2) && al2.TryGetProperty("FlightNumber", out var fn) ? fn.ToString() ?? "" : "",
                                        FromCity = seg.TryGetProperty("Origin", out var orig) && orig.TryGetProperty("CityCode", out var cc) ? cc.ToString() ?? "" : "",
                                        ToCity = seg.TryGetProperty("Destination", out var dest) && dest.TryGetProperty("CityCode", out var dc) ? dc.ToString() ?? "" : "",
                                        DepartureTime = seg.TryGetProperty("DepTime", out var dt) && DateTime.TryParse(dt.ToString(), out var dtv) ? dtv : DateTime.MinValue,
                                        ArrivalTime = seg.TryGetProperty("ArrTime", out var at) && DateTime.TryParse(at.ToString(), out var atv) ? atv : DateTime.MinValue,
                                        Pnr = pnr
                                    };
                                    reservation.Segments.Add(segObj);
                                }
                            }
                        }
                        catch { }
                    }

                    _dbContext.FlightReservations.Add(reservation);
                    await _dbContext.SaveChangesAsync();

                    if (request.Passengers != null && request.Passengers.Any())
                    {
                        var reservationPassengers = new List<FlightReservationPassenger>();
                        var responsePassengers = new List<JsonElement>();
                        if (resp.TryGetProperty("FlightItinerary", out var itineraryNode) && 
                            itineraryNode.TryGetProperty("Passenger", out var passArray) && 
                            passArray.ValueKind == JsonValueKind.Array)
                        {
                            responsePassengers = passArray.EnumerateArray().ToList();
                        }

                        for (int i = 0; i < request.Passengers.Count; i++)
                        {
                            var p = request.Passengers[i];
                            var passObj = new FlightReservationPassenger
                            {
                                FlightReservationId = reservation.Id,
                                FullName = $"{p.FirstName} {p.LastName}",
                                FirstName = p.FirstName,
                                LastName = p.LastName,
                                Title = p.Title,
                                PassportNo = p.PassportNo,
                                Nationality = p.CountryName,
                                Email = p.Email,
                                ContactNo = p.ContactNo,
                                DateOfBirth = DateTime.TryParse(p.DateOfBirth, out var dob1) ? dob1 : null,
                                PassengerType = p.PaxType == 1 ? "Adult" : p.PaxType == 2 ? "Child" : "Infant",
                                Gender = p.Gender == "1" ? "Male" : "Female",
                                SeatNumber = null // Re-assigned below
                            };

                            if (p.Seat != null && p.Seat.Any())
                            {
                                var rawSeats = p.Seat.Select(s => s.SeatNumber ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s));
                                passObj.SeatNumber = rawSeats.Any() ? string.Join(", ", rawSeats) : null;
                            }

                            if (i < responsePassengers.Count)
                            {
                                var matchedPax = responsePassengers.FirstOrDefault(r => 
                                    r.TryGetProperty("FirstName", out var fn) && fn.ToString()?.Equals(p.FirstName, StringComparison.OrdinalIgnoreCase) == true &&
                                    r.TryGetProperty("LastName", out var ln) && ln.ToString()?.Equals(p.LastName, StringComparison.OrdinalIgnoreCase) == true
                                );
                                
                                var rPax = matchedPax.ValueKind != JsonValueKind.Undefined ? matchedPax : responsePassengers[i];
                                
                                if (rPax.TryGetProperty("PaxId", out var paxIdNode))
                                {
                                    if (paxIdNode.ValueKind == JsonValueKind.Number)
                                        passObj.PaxId = paxIdNode.GetInt32();
                                    else if (paxIdNode.ValueKind == JsonValueKind.String && int.TryParse(paxIdNode.ToString(), out var parsedPaxId))
                                        passObj.PaxId = parsedPaxId;
                                }
                                
                                if (rPax.TryGetProperty("Ticket", out var tktNode))
                                {
                                    var tIdStr = tktNode.TryGetProperty("TicketId", out var tId) ? tId.ToString() : null;
                                    passObj.TicketId = string.IsNullOrWhiteSpace(tIdStr) ? null : tIdStr;

                                    var tNumStr = tktNode.TryGetProperty("TicketNumber", out var tNum) ? tNum.ToString() : null;
                                    passObj.TicketNumber = string.IsNullOrWhiteSpace(tNumStr) ? null : tNumStr;
                                }

                                if (rPax.TryGetProperty("SegmentAdditionalInfo", out var segInfo) && segInfo.ValueKind == JsonValueKind.Array)
                                {
                                    var confirmedSeats = segInfo.EnumerateArray()
                                        .Select(s => s.TryGetProperty("Seat", out var seatProp) ? seatProp.GetString() : null)
                                        .Where(s => !string.IsNullOrWhiteSpace(s));
                                    
                                    if (confirmedSeats.Any())
                                    {
                                        passObj.SeatNumber = string.Join(", ", confirmedSeats);
                                    }
                                }
                            }

                            decimal passSsrTotal = 0m;
                            if (i < responsePassengers.Count)
                            {
                                var rPaxNodeForFare = responsePassengers[i];
                                if (rPaxNodeForFare.TryGetProperty("Fare", out var paxFareNode) && paxFareNode.TryGetProperty("TotalSpecialServiceCharges", out var paxSsrNode) && paxSsrNode.ValueKind == JsonValueKind.Number)
                                {
                                    passSsrTotal = paxSsrNode.GetDecimal();
                                }
                            }

                            if (p.Baggage != null && p.Baggage.Any())
                            {
                                passObj.BaggageJson = System.Text.Json.JsonSerializer.Serialize(p.Baggage);
                            }

                            if (p.MealDynamic != null && p.MealDynamic.Any())
                            {
                                passObj.MealJson = System.Text.Json.JsonSerializer.Serialize(p.MealDynamic);
                            }
                            
                            passObj.SsrTotalInr = passSsrTotal;

                            reservationPassengers.Add(passObj);
                        }
                        _dbContext.FlightReservationPassengers.AddRange(reservationPassengers);
                        await _dbContext.SaveChangesAsync();
                    }

                    // If agent, deduct wallet
                    if (int.TryParse(userIdStr, out var agentId) && agentId > 0)
                    {
                        var user = await _dbContext.Users.FindAsync(agentId);
                        if (user != null && user.Role == AuthRoles.Agent)
                        {
                            if (isSuccess && !isPending && !isPriceChanged && ticketStatusCode == 1)
                            {
                                await _walletService.DebitWalletForBookingAsync(agentId, totalFare, reservation.BookingReference, "Flight", $"Flight Booking LCC PNR {pnr}");
                            }
                        }
                    }

                    // Dispatch email (only when ticket is confirmed, not pending)
                    if (!isPending)
                    {
                    try
                    {
                        global::User? agentInfo = null;
                        if (int.TryParse(userIdStr, out var aId) && aId > 0)
                        {
                            agentInfo = await _dbContext.Users.FindAsync(aId);
                        }
                        var emailReq = new SendFlightTicketEmailRequest
                        {
                            ToEmail = string.IsNullOrEmpty(reservation.PassengerEmail) ? (agentInfo?.Email ?? "") : reservation.PassengerEmail,
                            PassengerName = reservation.PassengerName,
                            BookingReference = reservation.BookingReference,
                            Airline = reservation.Airline,
                            Origin = reservation.FromCity,
                            Destination = reservation.ToCity,
                            DepartureTime = reservation.DepartureTime,
                            ArrivalTime = reservation.ArrivalTime,
                            Pnr = reservation.Pnr,
                            Price = reservation.TotalPriceInr,
                            Currency = "INR",
                            NonRefundable = reservation.NonRefundable,
                            CancellationCharges = reservation.CancellationCharges,
                                PartialSegmentCancellation = reservation.PartialSegmentCancellation,
                            AgentCompanyName = agentInfo?.CompanyName,
                            AgentLogoUrl = agentInfo?.AgentLogoUrl,
                            Passengers = await _dbContext.FlightReservationPassengers
                                            .Where(p => p.FlightReservationId == reservation.Id)
                                            .Select(p => new FlightPassengerTicketDto {
                                                FullName = p.FullName,
                                                PassengerType = p.PassengerType,
                                                Gender = p.Gender,
                                                SeatNumber = p.SeatNumber,
                                                TicketNumber = p.TicketNumber
                                            }).ToListAsync(),
                            Segments = reservation.Segments.Select(s => new FlightTicketSegmentDto {
                                Airline = s.Airline,
                                FlightNumber = s.FlightNumber,
                                FromCity = s.FromCity,
                                ToCity = s.ToCity,
                                DepartureTime = s.DepartureTime,
                                ArrivalTime = s.ArrivalTime,
                                Pnr = s.Pnr
                            }).ToList()
                        };
                        var backgroundJobQueue = HttpContext.RequestServices.GetRequiredService<PickNBook.Api.Services.IBackgroundJobQueue>();
                        backgroundJobQueue.QueueBackgroundWorkItem(async (sp, ct) =>
                        {
                            var scopedEmailService = sp.GetRequiredService<ITicketEmailService>();
                            await scopedEmailService.SendFlightTicketAsync(emailReq);
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send ticket email for Booking {BookingReference}", reservation.BookingReference);
                    }
                    } // end if (!isPending)
                }
                
                return Ok(outNode ?? (object)doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting TicketLCC.");
                return StatusCode(500, new { message = "Failed to get TicketLCC.", error = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("HoldGDS")]
        public async Task<IActionResult> HoldGDS([FromBody] FlightHoldGDSProxyRequestDto proxyRequest)
        {
            var request = new HoldGDSRequestDto
            {
                EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                SrdvType = proxyRequest.SrdvType,
                SrdvIndex = proxyRequest.SrdvIndex,
                TraceId = proxyRequest.TraceId,
                ResultIndex = proxyRequest.ResultIndex,
                CouponCode = proxyRequest.CouponCode,
                PromoCode = proxyRequest.PromoCode,
                PromotionId = proxyRequest.PromotionId,
                JourneyType = proxyRequest.JourneyType,
                Passengers = proxyRequest.Passengers
            };

            var passportValidationResult = ValidatePassengersPassport(request.Passengers);
            if (passportValidationResult != null) return passportValidationResult;

            try
            {
                var responseRaw = await _srdvFlightService.HoldGDSRawAsync(request);
                var outNode = JsonNode.Parse(responseRaw);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;
                
                bool isSuccess = false;
                bool isPending = false;
                JsonElement resp = root;
                if (root.TryGetProperty("Response", out var responseNode))
                {
                    resp = responseNode;
                }
                else if (root.TryGetProperty("Results", out var resultsNode))
                {
                    resp = resultsNode;
                }

                if (resp.TryGetProperty("ResponseStatus", out var status))
                {
                    if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                    if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                }
                
                var errSource = root.TryGetProperty("Error", out var rootErr) ? root : resp;
                if (errSource.TryGetProperty("Error", out var err) && err.TryGetProperty("ErrorCode", out var errCode))
                {
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;

                    // ErrorCode 10 = Pending (booking in process)
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 10) isPending = true;
                    if (errCode.ValueKind == JsonValueKind.String && errCode.ToString() == "10") isPending = true;
                }

                // Also detect pending from TicketStatus field
                if (resp.TryGetProperty("TicketStatus", out var tStatus) && tStatus.ToString()?.Equals("Pending", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isPending = true;
                }

                bool isPriceChanged = resp.TryGetProperty("IsPriceChanged", out var ipc) && ipc.ValueKind == JsonValueKind.True;
                int ticketStatusCode = resp.TryGetProperty("TicketStatus", out var tsCode) && tsCode.ValueKind == JsonValueKind.Number ? tsCode.GetInt32() : -1;
                bool ssrDenied = resp.TryGetProperty("SSRDenied", out var ssrDenNode) && ssrDenNode.ValueKind == JsonValueKind.True;
                string? ssrMessage = resp.TryGetProperty("SSRMessage", out var ssrMsgNode) && ssrMsgNode.ValueKind == JsonValueKind.String ? ssrMsgNode.GetString() : null;

                string pnr = resp.TryGetProperty("PNR", out var pnrProp) ? (pnrProp.ToString() ?? "") : "";
                if (string.IsNullOrEmpty(pnr) && root.TryGetProperty("PNR", out var rootPnrProp)) pnr = rootPnrProp.ToString() ?? "";

                string bookingId = resp.TryGetProperty("BookingId", out var bIdProp) ? (bIdProp.ToString() ?? "") : "";
                if (string.IsNullOrEmpty(bookingId) && root.TryGetProperty("BookingId", out var rootBIdProp)) bookingId = rootBIdProp.ToString() ?? "";

                if ((isSuccess || isPending) && (!string.IsNullOrEmpty(pnr) || !string.IsNullOrEmpty(bookingId)))
                {
                    decimal totalFare = 0, baseFare = 0, tax = 0, netFare = 0, customerFare = 0, ssrFromResponse = 0m;
                    string airline = "", flightNumber = "", fromCity = "", toCity = "";
                    DateTime depTime = DateTime.MinValue, arrTime = DateTime.MinValue;
                    bool nonRefundable = false;
                    string segmentsJson = "", fareRulesJson = "", travelClassStr = "Economy";

                    nonRefundable = resp.TryGetProperty("IsRefundable", out var isRef) && isRef.ValueKind == JsonValueKind.True ? false : true;

                    if (resp.TryGetProperty("FareRules", out var fr))
                        fareRulesJson = fr.ToString();
                    else if (resp.TryGetProperty("MiniFareRules", out var mfr))
                        fareRulesJson = mfr.ToString();

                    if (resp.TryGetProperty("FlightItinerary", out var itinerary))
                    {
                        if (itinerary.TryGetProperty("Fare", out var fare))
                        {
                            totalFare = fare.TryGetProperty("PublishedFare", out var pubFare) && pubFare.ValueKind == JsonValueKind.Number ? pubFare.GetDecimal() : 0;
                            baseFare = fare.TryGetProperty("BaseFare", out var bFare) && bFare.ValueKind == JsonValueKind.Number ? bFare.GetDecimal() : 0;
                            tax = fare.TryGetProperty("Tax", out var tFare) && tFare.ValueKind == JsonValueKind.Number ? tFare.GetDecimal() : 0;
                            customerFare = totalFare;
                            netFare = fare.TryGetProperty("OfferedFare", out var offFare) && offFare.ValueKind == JsonValueKind.Number ? offFare.GetDecimal() : totalFare;

                            decimal totalBaggage = 0, totalMeal = 0, totalSeat = 0, totalSsr = 0;
                            if (fare.TryGetProperty("TotalBaggageCharges", out var bagNode))
                            {
                                if (bagNode.ValueKind == JsonValueKind.Number) totalBaggage = bagNode.GetDecimal();
                                else if (bagNode.ValueKind == JsonValueKind.String && decimal.TryParse(bagNode.GetString(), out var parsedBag)) totalBaggage = parsedBag;
                            }
                            if (fare.TryGetProperty("TotalMealCharges", out var mealNode))
                            {
                                if (mealNode.ValueKind == JsonValueKind.Number) totalMeal = mealNode.GetDecimal();
                                else if (mealNode.ValueKind == JsonValueKind.String && decimal.TryParse(mealNode.GetString(), out var parsedMeal)) totalMeal = parsedMeal;
                            }
                            if (fare.TryGetProperty("TotalSeatCharges", out var seatNode))
                            {
                                if (seatNode.ValueKind == JsonValueKind.Number) totalSeat = seatNode.GetDecimal();
                                else if (seatNode.ValueKind == JsonValueKind.String && decimal.TryParse(seatNode.GetString(), out var parsedSeat)) totalSeat = parsedSeat;
                            }
                            if (fare.TryGetProperty("TotalSpecialServiceCharges", out var ssrNode))
                            {
                                if (ssrNode.ValueKind == JsonValueKind.Number) totalSsr = ssrNode.GetDecimal();
                                else if (ssrNode.ValueKind == JsonValueKind.String && decimal.TryParse(ssrNode.GetString(), out var parsedSsr)) totalSsr = parsedSsr;
                            }
                            ssrFromResponse = totalBaggage + totalMeal + totalSeat + totalSsr;
                        }

                        if (itinerary.TryGetProperty("Segments", out var segs) && segs.ValueKind == JsonValueKind.Array && segs.GetArrayLength() > 0)
                        {
                            segmentsJson = segs.ToString();
                            var firstSeg = segs[0];
                            if (firstSeg.TryGetProperty("Airline", out var alNode) && alNode.TryGetProperty("AirlineName", out var alNameNode))
                                airline = alNameNode.ToString() ?? "";
                            if (firstSeg.TryGetProperty("Airline", out var alNode2) && alNode2.TryGetProperty("FlightNumber", out var fnNode))
                                flightNumber = fnNode.ToString() ?? "";
                            
                            // GDS responses nest city info under Origin/Destination directly (not Origin.Airport)
                            if (firstSeg.TryGetProperty("Origin", out var orig))
                            {
                                // Try Origin.Airport.CityName first (search-style), then Origin.CityName (GDS-style)
                                if (orig.TryGetProperty("Airport", out var origApt) && origApt.TryGetProperty("CityName", out var origCity))
                                    fromCity = origCity.ToString() ?? "";
                                else if (orig.TryGetProperty("CityName", out var origCityDirect))
                                    fromCity = origCityDirect.ToString() ?? "";
                            }
                            if (firstSeg.TryGetProperty("Destination", out var dest))
                            {
                                if (dest.TryGetProperty("Airport", out var destApt) && destApt.TryGetProperty("CityName", out var destCity))
                                    toCity = destCity.ToString() ?? "";
                                else if (dest.TryGetProperty("CityName", out var destCityDirect))
                                    toCity = destCityDirect.ToString() ?? "";
                            }
                            
                            // DepTime/ArrTime are at segment level in GDS responses, not nested under Origin/Destination
                            if (firstSeg.TryGetProperty("DepTime", out var dTime) && DateTime.TryParse(dTime.ToString(), out var parsedDep))
                                depTime = parsedDep;
                            else if (firstSeg.TryGetProperty("Origin", out var dep) && dep.TryGetProperty("DepTime", out var dTime2) && DateTime.TryParse(dTime2.ToString(), out var parsedDep2))
                                depTime = parsedDep2;
                            if (firstSeg.TryGetProperty("ArrTime", out var aTime) && DateTime.TryParse(aTime.ToString(), out var parsedArr))
                                arrTime = parsedArr;
                            else if (firstSeg.TryGetProperty("Destination", out var arr) && arr.TryGetProperty("ArrTime", out var aTime2) && DateTime.TryParse(aTime2.ToString(), out var parsedArr2))
                                arrTime = parsedArr2;
                            if (firstSeg.TryGetProperty("CabinClass", out var cClass) && cClass.ValueKind == JsonValueKind.Number)
                            {
                                travelClassStr = cClass.GetInt32() switch
                                {
                                    2 => "Economy",
                                    3 => "PremiumEconomy",
                                    4 => "Business",
                                    5 => "PremiumBusiness",
                                    6 => "First",
                                    _ => "Economy"
                                };
                            }
                        }
                    }

                    var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                    
                    var firstPax = request.Passengers?.FirstOrDefault();
                    string paxName = firstPax != null ? $"{firstPax.FirstName} {firstPax.LastName}" : "";
                    string paxPhone = firstPax != null ? firstPax.ContactNo : "";
                    string paxEmail = firstPax != null ? firstPax.Email : "";
                    int adults = request.Passengers?.Count(p => p.PaxType == 1) ?? 0;
                    int children = request.Passengers?.Count(p => p.PaxType == 2) ?? 0;
                    int infants = request.Passengers?.Count(p => p.PaxType == 3) ?? 0;
                    int seatsBooked = adults + children;

                    TripType parsedTripType = TripType.OneWay;
                    if (request.JourneyType.HasValue)
                    {
                        parsedTripType = request.JourneyType == 2 ? TripType.RoundTrip : (request.JourneyType == 3 ? TripType.MultiCity : TripType.OneWay);
                    }
                    else if (!string.IsNullOrEmpty(request.ResultIndex) && request.ResultIndex.Contains(","))
                    {
                        parsedTripType = TripType.RoundTrip;
                    }
                    else
                    {
                        parsedTripType = (resp.TryGetProperty("FlightItinerary", out var gdsIt) && gdsIt.TryGetProperty("Segments", out var gdsSegs) && gdsSegs.ValueKind == JsonValueKind.Array && gdsSegs.GetArrayLength() > 1) ? TripType.RoundTrip : TripType.OneWay;
                    }

                    var pricingBreakdown = await _pricingService.CalculatePricingAsync(
                        supplierBaseFare: baseFare,
                        supplierTaxAmount: tax,
                        airlineCode: airline,
                        airlineName: airline,
                        origin: fromCity,
                        destination: toCity,
                        departureDate: depTime,
                        travelClass: travelClassStr,
                        tripType: parsedTripType,
                        passengerCount: adults + children + infants,
                        couponCode: request.CouponCode,
                        userId: userIdStr,
                        selectedPromotionId: request.PromotionId
                    );

                    if (outNode != null)
                    {
                        var respObj = outNode["Response"] ?? outNode["Results"] ?? outNode;
                        var fareNode = respObj["FlightItinerary"]?["Fare"];
                        if (fareNode != null)
                        {
                            fareNode["B2CFinalFare"] = pricingBreakdown.FinalAmount + ssrFromResponse;
                            fareNode["B2CPublishedFare"] = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount + ssrFromResponse;
                            fareNode["B2CMarkupAmount"] = pricingBreakdown.MarkupAmount;
                        }
                    }

                    var reservation = new FlightReservation
                    {
                        BookingReference = $"FL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                        Pnr = pnr,
                        UserId = userIdStr,
                        Status = isPending ? "Pending" : "Hold",
                        BookedAtUtc = DateTime.UtcNow,
                        SSRDenied = ssrDenied,
                        SSRMessage = ssrMessage,
                        
                        TraceId = resp.TryGetProperty("TraceId", out var newTraceId) && newTraceId.ValueKind == JsonValueKind.String ? newTraceId.GetString() ?? request.TraceId : request.TraceId,
                        ResultIndex = request.ResultIndex,
                        FlightNumber = flightNumber,
                        Airline = airline,
                        FromCity = fromCity,
                        ToCity = toCity,
                        DepartureTime = depTime,
                        ArrivalTime = arrTime,
                        SegmentsJson = segmentsJson,
                        
                        NonRefundable = nonRefundable,
                        FareRulesJson = fareRulesJson,

                        TotalPriceInr = pricingBreakdown.FinalAmount + ssrFromResponse,
                        CustomerFareInr = pricingBreakdown.FinalAmount + ssrFromResponse,
                        NetFareInr = netFare,
                        SupplierBaseFare = baseFare,
                        SupplierTaxAmount = tax,
                        SupplierTotalFare = totalFare,
                        SsrAmountInr = ssrFromResponse,
                        MarkupAmount = pricingBreakdown.MarkupAmount,
                        B2CPublishedFareInr = pricingBreakdown.SupplierTotalFare + pricingBreakdown.MarkupAmount,
                        B2CMarkupAmountInr = pricingBreakdown.MarkupAmount,
                        B2CDiscountAmountInr = pricingBreakdown.PromotionDiscount + pricingBreakdown.CouponDiscount,
                        PromotionDiscount = pricingBreakdown.PromotionDiscount,
                        CouponDiscount = pricingBreakdown.CouponDiscount,
                        SrdvTicketResponseJson = responseRaw,
                        
                        PassengerName = paxName,
                        PassengerPhone = paxPhone,
                        PassengerEmail = paxEmail,
                        Adults = adults,
                        Children = children,
                        Infants = infants,
                        SeatsBooked = seatsBooked,
                        
                        SrdvBookingId = resp.TryGetProperty("BookingId", out var bId) ? bId.ToString() : null,
                        SrdvPnr = pnr,
                        TicketStatus = resp.TryGetProperty("TicketStatus", out var ts) ? ts.ToString() : null,
                        IsLcc = false,
                        SrdvType = request.SrdvType,
                        SrdvIndex = request.SrdvIndex,
                        ReturnPnr = resp.TryGetProperty("ReturnPNR", out var rpNode) ? rpNode.ToString() : null
                    };

                    if (!string.IsNullOrEmpty(segmentsJson))
                    {
                        try
                        {
                            var parsedSegments = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(segmentsJson);
                            if (parsedSegments != null)
                            {
                                foreach (var seg in parsedSegments)
                                {
                                    var segObj = new PickNBook.Api.Models.FlightReservationSegment
                                    {
                                        TripIndicator = seg.TryGetProperty("TripIndicator", out var ti) && ti.ValueKind == System.Text.Json.JsonValueKind.Number ? ti.GetInt32() : 0,
                                        SegmentIndicator = seg.TryGetProperty("SegmentIndicator", out var si) && si.ValueKind == System.Text.Json.JsonValueKind.Number ? si.GetInt32() : 0,
                                        Baggage = seg.TryGetProperty("Baggage", out var bag) ? bag.ToString() : null,
                                        CabinBaggage = seg.TryGetProperty("CabinBaggage", out var cBag) ? cBag.ToString() : null,
                                        Duration = seg.TryGetProperty("Duration", out var dur) && dur.ValueKind == System.Text.Json.JsonValueKind.Number ? dur.GetInt32() : 0,
                                        Airline = seg.TryGetProperty("Airline", out var al) && al.TryGetProperty("AirlineName", out var aln) ? aln.ToString() ?? "" : "",
                                        FlightNumber = seg.TryGetProperty("Airline", out var al2) && al2.TryGetProperty("FlightNumber", out var fn) ? fn.ToString() ?? "" : "",
                                        FromCity = seg.TryGetProperty("Origin", out var orig) && orig.TryGetProperty("CityCode", out var cc) ? cc.ToString() ?? "" : "",
                                        ToCity = seg.TryGetProperty("Destination", out var dest) && dest.TryGetProperty("CityCode", out var dc) ? dc.ToString() ?? "" : "",
                                        DepartureTime = seg.TryGetProperty("DepTime", out var dt) && DateTime.TryParse(dt.ToString(), out var dtv) ? dtv : DateTime.MinValue,
                                        ArrivalTime = seg.TryGetProperty("ArrTime", out var at) && DateTime.TryParse(at.ToString(), out var atv) ? atv : DateTime.MinValue,
                                        Pnr = pnr
                                    };
                                    reservation.Segments.Add(segObj);
                                }
                            }
                        }
                        catch { }
                    }

                    _dbContext.FlightReservations.Add(reservation);
                    await _dbContext.SaveChangesAsync();

                    if (request.Passengers != null && request.Passengers.Any())
                    {
                        var reservationPassengers = new List<FlightReservationPassenger>();
                        var responsePassengers = new List<JsonElement>();
                        if (resp.TryGetProperty("FlightItinerary", out var itineraryNode) && 
                            itineraryNode.TryGetProperty("Passenger", out var passArray) && 
                            passArray.ValueKind == JsonValueKind.Array)
                        {
                            responsePassengers = passArray.EnumerateArray().ToList();
                        }

                        for (int i = 0; i < request.Passengers.Count; i++)
                        {
                            var p = request.Passengers[i];
                            var passObj = new FlightReservationPassenger
                            {
                                FlightReservationId = reservation.Id,
                                FullName = $"{p.FirstName} {p.LastName}",
                                FirstName = p.FirstName,
                                LastName = p.LastName,
                                Title = p.Title,
                                PassportNo = p.PassportNo,
                                Nationality = p.CountryName,
                                Email = p.Email,
                                ContactNo = p.ContactNo,
                                DateOfBirth = DateTime.TryParse(p.DateOfBirth, out var dob2) ? dob2 : null,
                                PassengerType = p.PaxType == 1 ? "Adult" : p.PaxType == 2 ? "Child" : "Infant",
                                Gender = p.Gender == "1" ? "Male" : "Female",
                                SeatNumber = p.Seat != null && p.Seat.Any() ? string.Join(", ", p.Seat.Select(s => s.SeatNumber)) : null
                            };

                            if (i < responsePassengers.Count)
                            {
                                var rPax = responsePassengers[i];
                                passObj.PaxId = rPax.TryGetProperty("PaxId", out var paxIdNode) && paxIdNode.ValueKind == JsonValueKind.Number ? paxIdNode.GetInt32() : null;
                                
                                if (rPax.TryGetProperty("Ticket", out var tktNode))
                                {
                                    passObj.TicketId = tktNode.TryGetProperty("TicketId", out var tId) ? tId.ToString() : null;
                                    passObj.TicketNumber = tktNode.TryGetProperty("TicketNumber", out var tNum) ? tNum.ToString() : null;
                                }
                            }

                            decimal passSsrTotal = 0m;
                            if (i < responsePassengers.Count)
                            {
                                var rPax = responsePassengers[i];
                                if (rPax.TryGetProperty("Fare", out var paxFareNode) && paxFareNode.TryGetProperty("TotalSpecialServiceCharges", out var paxSsrNode) && paxSsrNode.ValueKind == JsonValueKind.Number)
                                {
                                    passSsrTotal = paxSsrNode.GetDecimal();
                                }
                            }

                            if (p.Baggage != null && p.Baggage.Any())
                            {
                                passObj.BaggageJson = System.Text.Json.JsonSerializer.Serialize(p.Baggage);
                            }

                            if (p.MealDynamic != null && p.MealDynamic.Any())
                            {
                                passObj.MealJson = System.Text.Json.JsonSerializer.Serialize(p.MealDynamic);
                            }
                            
                            passObj.SsrTotalInr = passSsrTotal;

                            reservationPassengers.Add(passObj);
                        }
                        _dbContext.FlightReservationPassengers.AddRange(reservationPassengers);
                        await _dbContext.SaveChangesAsync();
                    }
                }

                return Ok(outNode ?? (object)doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting HoldGDS.");
                return StatusCode(500, new { message = "Failed to get HoldGDS.", error = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("TicketGDS")]
        public async Task<IActionResult> TicketGDS([FromBody] FlightTicketGDSProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new TicketGDSRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType,
                    SrdvIndex = proxyRequest.SrdvIndex,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex,
                    PNR = proxyRequest.PNR,
                    BookingId = proxyRequest.BookingId,
                    CouponCode = proxyRequest.CouponCode,
                    PromoCode = proxyRequest.PromoCode,
                    PromotionId = proxyRequest.PromotionId,
                    Passengers = proxyRequest.Passengers
                };
                var responseRaw = await _srdvFlightService.TicketGDSRawAsync(request);
                var outNode = JsonNode.Parse(responseRaw);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;

                bool isSuccess = false;
                bool isPending = false;
                JsonElement resp = root;
                if (root.TryGetProperty("Response", out var responseNode))
                {
                    resp = responseNode;
                }
                else if (root.TryGetProperty("Results", out var resultsNode))
                {
                    resp = resultsNode;
                }

                if (resp.TryGetProperty("ResponseStatus", out var status))
                {
                    if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                    if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                }
                
                var errSource = root.TryGetProperty("Error", out var rootErr) ? root : resp;
                if (errSource.TryGetProperty("Error", out var err) && err.TryGetProperty("ErrorCode", out var errCode))
                {
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;

                    // ErrorCode 10 = Pending (booking in process)
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 10) isPending = true;
                    if (errCode.ValueKind == JsonValueKind.String && errCode.ToString() == "10") isPending = true;
                }

                // Also detect pending from TicketStatus field
                if (resp.TryGetProperty("TicketStatus", out var tStatus) && tStatus.ToString()?.Equals("Pending", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isPending = true;
                }

                bool isPriceChanged = resp.TryGetProperty("IsPriceChanged", out var ipc) && ipc.ValueKind == JsonValueKind.True;
                int ticketStatusCode = resp.TryGetProperty("TicketStatus", out var tsCode) && tsCode.ValueKind == JsonValueKind.Number ? tsCode.GetInt32() : -1;
                bool ssrDenied = resp.TryGetProperty("SSRDenied", out var ssrDenNode) && ssrDenNode.ValueKind == JsonValueKind.True;
                string? ssrMessage = resp.TryGetProperty("SSRMessage", out var ssrMsgNode) && ssrMsgNode.ValueKind == JsonValueKind.String ? ssrMsgNode.GetString() : null;

                string pnr = resp.TryGetProperty("PNR", out var pnrProp) ? (pnrProp.ToString() ?? "") : request.PNR;
                if (string.IsNullOrEmpty(pnr) && root.TryGetProperty("PNR", out var rootPnrProp)) pnr = rootPnrProp.ToString() ?? "";
                if (string.IsNullOrEmpty(pnr)) pnr = request.PNR;

                if ((isSuccess || isPending) && !string.IsNullOrEmpty(pnr))
                {

                    // Look up by SrdvBookingId first (stable across Hold→Ticket), then fall back to PNR
                    string bookingIdFromResp = resp.TryGetProperty("BookingId", out var bIdLookup) ? bIdLookup.ToString() : "";
                    var reservation = !string.IsNullOrEmpty(bookingIdFromResp)
                        ? await _dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(r => r.SrdvBookingId == bookingIdFromResp)
                        : null;
                    if (reservation == null)
                        reservation = await _dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(r => r.Pnr == pnr);
                    if (reservation != null)
                    {
                        if (outNode != null)
                        {
                            var respObj = outNode["Response"] ?? outNode["Results"] ?? outNode;
                            var fareNode = respObj["FlightItinerary"]?["Fare"];
                            if (fareNode != null)
                            {
                                decimal totalBaggage = 0, totalMeal = 0, totalSeat = 0, totalSsr = 0;
                                if (decimal.TryParse(fareNode["TotalBaggageCharges"]?.ToString(), out var parsedBag)) totalBaggage = parsedBag;
                                if (decimal.TryParse(fareNode["TotalMealCharges"]?.ToString(), out var parsedMeal)) totalMeal = parsedMeal;
                                if (decimal.TryParse(fareNode["TotalSeatCharges"]?.ToString(), out var parsedSeat)) totalSeat = parsedSeat;
                                if (decimal.TryParse(fareNode["TotalSpecialServiceCharges"]?.ToString(), out var parsedSsr)) totalSsr = parsedSsr;
                                decimal ssrFromResponse = totalBaggage + totalMeal + totalSeat + totalSsr;

                                decimal baseCustomerFare = reservation.CustomerFareInr - reservation.SsrAmountInr;
                                decimal basePublishedFare = reservation.B2CPublishedFareInr - reservation.SsrAmountInr;

                                fareNode["B2CFinalFare"] = baseCustomerFare + ssrFromResponse;
                                fareNode["B2CPublishedFare"] = basePublishedFare + ssrFromResponse;
                                fareNode["B2CMarkupAmount"] = reservation.MarkupAmount;
                            }
                        }

                        reservation.Status = isPending ? "Pending" : "Booked";
                        reservation.SrdvTicketResponseJson = responseRaw;

                        reservation.SrdvBookingId = resp.TryGetProperty("BookingId", out var bId) ? bId.ToString() : reservation.SrdvBookingId;
                        reservation.TicketStatus = resp.TryGetProperty("TicketStatus", out var ts) ? ts.ToString() : reservation.TicketStatus;
                        reservation.SSRDenied = ssrDenied;
                        reservation.SSRMessage = ssrMessage;

                        var responsePassengers = new List<JsonElement>();
                        if (resp.TryGetProperty("FlightItinerary", out var itineraryNode) && 
                            itineraryNode.TryGetProperty("Passenger", out var passArray) && 
                            passArray.ValueKind == JsonValueKind.Array)
                        {
                            responsePassengers = passArray.EnumerateArray().ToList();
                        }

                        var existingPassengers = await _dbContext.FlightReservationPassengers
                                                 .Where(p => p.FlightReservationId == reservation.Id)
                                                 .OrderBy(p => p.Id)
                                                 .ToListAsync();
                        
                        for (int i = 0; i < existingPassengers.Count; i++)
                        {
                            if (i < responsePassengers.Count)
                            {
                                var rPax = responsePassengers[i];
                                if (rPax.TryGetProperty("PaxId", out var paxIdNode) && paxIdNode.ValueKind == JsonValueKind.Number)
                                {
                                    existingPassengers[i].PaxId = paxIdNode.GetInt32();
                                }
                                if (rPax.TryGetProperty("Ticket", out var tktNode))
                                {
                                    if (tktNode.TryGetProperty("TicketId", out var tId))
                                        existingPassengers[i].TicketId = tId.ToString();
                                    if (tktNode.TryGetProperty("TicketNumber", out var tNum))
                                        existingPassengers[i].TicketNumber = tNum.ToString();
                                }
                            }
                        }

                        await _dbContext.SaveChangesAsync();

                        // If agent, deduct wallet
                        if (int.TryParse(reservation.UserId, out var agentId) && agentId > 0)
                        {
                            var user = await _dbContext.Users.FindAsync(agentId);
                            if (user != null && user.Role == AuthRoles.Agent)
                            {
                                if (isSuccess && !isPending && !isPriceChanged && ticketStatusCode == 1)
                                {
                                    await _walletService.DebitWalletForBookingAsync(agentId, reservation.SupplierTotalFare, reservation.BookingReference, "Flight", $"Flight Booking GDS PNR {pnr}");
                                }
                            }
                        }

                        // Dispatch email (only when ticket is confirmed, not pending)
                        if (!isPending)
                        {
                        try
                        {
                            global::User? agentInfo = null;
                            if (int.TryParse(reservation.UserId, out var aId) && aId > 0)
                            {
                                agentInfo = await _dbContext.Users.FindAsync(aId);
                            }
                            var emailReq = new SendFlightTicketEmailRequest
                            {
                                ToEmail = string.IsNullOrEmpty(reservation.PassengerEmail) ? (agentInfo?.Email ?? "") : reservation.PassengerEmail,
                                PassengerName = reservation.PassengerName,
                                BookingReference = reservation.BookingReference,
                                Airline = reservation.Airline,
                                Origin = reservation.FromCity,
                                Destination = reservation.ToCity,
                                DepartureTime = reservation.DepartureTime,
                                ArrivalTime = reservation.ArrivalTime,
                                Pnr = reservation.Pnr,
                                Price = reservation.TotalPriceInr,
                                Currency = "INR",
                                NonRefundable = reservation.NonRefundable,
                                CancellationCharges = reservation.CancellationCharges,
                                PartialSegmentCancellation = reservation.PartialSegmentCancellation,
                                AgentCompanyName = agentInfo?.CompanyName,
                                AgentLogoUrl = agentInfo?.AgentLogoUrl,
                                Passengers = await _dbContext.FlightReservationPassengers
                                                .Where(p => p.FlightReservationId == reservation.Id)
                                                .Select(p => new FlightPassengerTicketDto {
                                                    FullName = p.FullName,
                                                    PassengerType = p.PassengerType,
                                                    Gender = p.Gender,
                                                    SeatNumber = p.SeatNumber,
                                                    TicketNumber = p.TicketNumber
                                                }).ToListAsync(),
                                Segments = reservation.Segments.Select(s => new FlightTicketSegmentDto {
                                    Airline = s.Airline,
                                    FlightNumber = s.FlightNumber,
                                    FromCity = s.FromCity,
                                    ToCity = s.ToCity,
                                    DepartureTime = s.DepartureTime,
                                    ArrivalTime = s.ArrivalTime,
                                    Pnr = s.Pnr
                                }).ToList()
                            };
                            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                            _ = Task.Run(async () =>
                            {
                                using var scope = scopeFactory.CreateScope();
                                var scopedEmailService = scope.ServiceProvider.GetRequiredService<ITicketEmailService>();
                                await scopedEmailService.SendFlightTicketAsync(emailReq);
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send ticket email for Booking {BookingReference}", reservation.BookingReference);
                        }
                        } // end if (!isPending)
                    }
                    else
                    {
                    decimal totalFare = 0, baseFare = 0, tax = 0, netFare = 0, customerFare = 0, ssrFromResponse = 0m;
                    string airline = "", flightNumber = "", fromCity = "", toCity = "";
                    DateTime depTime = DateTime.MinValue, arrTime = DateTime.MinValue;
                    bool nonRefundable = false;
                    string segmentsJson = "", fareRulesJson = "", travelClassStr = "Economy";

                    nonRefundable = resp.TryGetProperty("IsRefundable", out var isRef) && isRef.ValueKind == JsonValueKind.True ? false : true;

                    if (resp.TryGetProperty("FareRules", out var fr))
                        fareRulesJson = fr.ToString();
                    else if (resp.TryGetProperty("MiniFareRules", out var mfr))
                        fareRulesJson = mfr.ToString();

                    if (resp.TryGetProperty("FlightItinerary", out var itinerary))
                    {
                        if (itinerary.TryGetProperty("Fare", out var fare))
                        {
                            totalFare = fare.TryGetProperty("PublishedFare", out var pubFare) && pubFare.ValueKind == JsonValueKind.Number ? pubFare.GetDecimal() : 0;
                            baseFare = fare.TryGetProperty("BaseFare", out var bFare) && bFare.ValueKind == JsonValueKind.Number ? bFare.GetDecimal() : 0;
                            tax = fare.TryGetProperty("Tax", out var tFare) && tFare.ValueKind == JsonValueKind.Number ? tFare.GetDecimal() : 0;
                            customerFare = totalFare;
                            netFare = fare.TryGetProperty("OfferedFare", out var offFare) && offFare.ValueKind == JsonValueKind.Number ? offFare.GetDecimal() : totalFare;

                            if (fare.TryGetProperty("TotalSpecialServiceCharges", out var ssrNode))
                            {
                                if (ssrNode.ValueKind == JsonValueKind.Number)
                                    ssrFromResponse = ssrNode.GetDecimal();
                                else if (ssrNode.ValueKind == JsonValueKind.String && decimal.TryParse(ssrNode.GetString(), out var parsedSsr))
                                    ssrFromResponse = parsedSsr;
                            }
                        }

                        if (itinerary.TryGetProperty("Segments", out var segs) && segs.ValueKind == JsonValueKind.Array && segs.GetArrayLength() > 0)
                        {
                            segmentsJson = segs.ToString();
                            var firstSeg = segs[0];
                            if (firstSeg.TryGetProperty("Airline", out var alNode) && alNode.TryGetProperty("AirlineName", out var alNameNode))
                                airline = alNameNode.ToString() ?? "";
                            if (firstSeg.TryGetProperty("Airline", out var alNode2) && alNode2.TryGetProperty("FlightNumber", out var fnNode))
                                flightNumber = fnNode.ToString() ?? "";
                            
                            if (firstSeg.TryGetProperty("Origin", out var orig) && orig.TryGetProperty("Airport", out var origApt) && origApt.TryGetProperty("CityName", out var origCity))
                                fromCity = origCity.ToString() ?? "";
                            if (firstSeg.TryGetProperty("Destination", out var dest) && dest.TryGetProperty("Airport", out var destApt) && destApt.TryGetProperty("CityName", out var destCity))
                                toCity = destCity.ToString() ?? "";
                            
                            if (firstSeg.TryGetProperty("Origin", out var dep) && dep.TryGetProperty("DepTime", out var dTime) && DateTime.TryParse(dTime.ToString(), out var parsedDep))
                                depTime = parsedDep;
                            if (firstSeg.TryGetProperty("Destination", out var arr) && arr.TryGetProperty("ArrTime", out var aTime) && DateTime.TryParse(aTime.ToString(), out var parsedArr))
                                arrTime = parsedArr;
                            if (firstSeg.TryGetProperty("CabinClass", out var cClass) && cClass.ValueKind == JsonValueKind.Number)
                            {
                                travelClassStr = cClass.GetInt32() switch
                                {
                                    2 => "Economy",
                                    3 => "PremiumEconomy",
                                    4 => "Business",
                                    5 => "PremiumBusiness",
                                    6 => "First",
                                    _ => "Economy"
                                };
                            }
                        }
                    }

                    var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                    
                    var firstPax = request.Passengers?.FirstOrDefault();
                    string paxName = firstPax != null ? $"{firstPax.FirstName} {firstPax.LastName}" : "";
                    string paxPhone = firstPax != null ? firstPax.ContactNo : "";
                    string paxEmail = firstPax != null ? firstPax.Email : "";
                    int adults = request.Passengers?.Count(p => p.PaxType == 1) ?? 0;
                    int children = request.Passengers?.Count(p => p.PaxType == 2) ?? 0;
                    int infants = request.Passengers?.Count(p => p.PaxType == 3) ?? 0;
                    int seatsBooked = adults + children;

                    var pricingBreakdown = await _pricingService.CalculatePricingAsync(
                        supplierBaseFare: baseFare,
                        supplierTaxAmount: tax,
                        airlineCode: airline,
                        airlineName: airline,
                        origin: fromCity,
                        destination: toCity,
                        departureDate: depTime,
                        travelClass: travelClassStr,
                        tripType: TripType.OneWay,
                        passengerCount: adults + children + infants,
                        couponCode: request.CouponCode,
                        userId: userIdStr
                    );

                    if (outNode != null)
                    {
                        var respObj = outNode["Response"] ?? outNode["Results"] ?? outNode;
                        var fareNode = respObj["FlightItinerary"]?["Fare"];
                        if (fareNode != null)
                        {
                            fareNode["B2CFinalFare"] = pricingBreakdown.FinalAmount + ssrFromResponse;
                            fareNode["B2CMarkup"] = pricingBreakdown.MarkupAmount;
                        }
                    }

                    var newReservation = new FlightReservation
                    {
                        BookingReference = $"FL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                        Pnr = pnr,
                        UserId = userIdStr,
                        Status = isPending ? "Pending" : "Booked",
                        BookedAtUtc = DateTime.UtcNow,
                        
                        TraceId = resp.TryGetProperty("TraceId", out var newTraceId) && newTraceId.ValueKind == JsonValueKind.String ? newTraceId.GetString() ?? request.TraceId : request.TraceId,
                        ResultIndex = request.ResultIndex,
                        FlightNumber = flightNumber,
                        Airline = airline,
                        FromCity = fromCity,
                        ToCity = toCity,
                        DepartureTime = depTime,
                        ArrivalTime = arrTime,
                        SegmentsJson = segmentsJson,
                        
                        NonRefundable = nonRefundable,
                        FareRulesJson = fareRulesJson,

                        TotalPriceInr = pricingBreakdown.FinalAmount + ssrFromResponse,
                        CustomerFareInr = pricingBreakdown.FinalAmount + ssrFromResponse,
                        NetFareInr = netFare,
                        SupplierBaseFare = baseFare,
                        SupplierTaxAmount = tax,
                        SupplierTotalFare = totalFare,
                        SsrAmountInr = ssrFromResponse,
                        MarkupAmount = pricingBreakdown.MarkupAmount,
                        PromotionDiscount = pricingBreakdown.PromotionDiscount,
                        CouponDiscount = pricingBreakdown.CouponDiscount,
                        SrdvTicketResponseJson = responseRaw,
                        
                        PassengerName = paxName,
                        PassengerPhone = paxPhone,
                        PassengerEmail = paxEmail,
                        Adults = adults,
                        Children = children,
                        Infants = infants,
                        SeatsBooked = seatsBooked,
                        
                        SrdvBookingId = resp.TryGetProperty("BookingId", out var bId) ? bId.ToString() : null,
                        SrdvPnr = pnr,
                        TicketStatus = resp.TryGetProperty("TicketStatus", out var ts) ? ts.ToString() : null,
                        IsLcc = false,
                        SrdvType = request.SrdvType,
                        SrdvIndex = request.SrdvIndex,
                        ReturnPnr = resp.TryGetProperty("ReturnPNR", out var rpNode) ? rpNode.ToString() : null
                    };

                    if (!string.IsNullOrEmpty(segmentsJson))
                    {
                        try
                        {
                            var parsedSegments = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(segmentsJson);
                            if (parsedSegments != null)
                            {
                                foreach (var seg in parsedSegments)
                                {
                                    var segObj = new PickNBook.Api.Models.FlightReservationSegment
                                    {
                                        TripIndicator = seg.TryGetProperty("TripIndicator", out var ti) && ti.ValueKind == System.Text.Json.JsonValueKind.Number ? ti.GetInt32() : 0,
                                        SegmentIndicator = seg.TryGetProperty("SegmentIndicator", out var si) && si.ValueKind == System.Text.Json.JsonValueKind.Number ? si.GetInt32() : 0,
                                        Baggage = seg.TryGetProperty("Baggage", out var bag) ? bag.ToString() : null,
                                        CabinBaggage = seg.TryGetProperty("CabinBaggage", out var cBag) ? cBag.ToString() : null,
                                        Duration = seg.TryGetProperty("Duration", out var dur) && dur.ValueKind == System.Text.Json.JsonValueKind.Number ? dur.GetInt32() : 0,
                                        Airline = seg.TryGetProperty("Airline", out var al) && al.TryGetProperty("AirlineName", out var aln) ? aln.ToString() ?? "" : "",
                                        FlightNumber = seg.TryGetProperty("Airline", out var al2) && al2.TryGetProperty("FlightNumber", out var fn) ? fn.ToString() ?? "" : "",
                                        FromCity = seg.TryGetProperty("Origin", out var orig) && orig.TryGetProperty("CityCode", out var cc) ? cc.ToString() ?? "" : "",
                                        ToCity = seg.TryGetProperty("Destination", out var dest) && dest.TryGetProperty("CityCode", out var dc) ? dc.ToString() ?? "" : "",
                                        DepartureTime = seg.TryGetProperty("DepTime", out var dt) && DateTime.TryParse(dt.ToString(), out var dtv) ? dtv : DateTime.MinValue,
                                        ArrivalTime = seg.TryGetProperty("ArrTime", out var at) && DateTime.TryParse(at.ToString(), out var atv) ? atv : DateTime.MinValue,
                                        Pnr = pnr
                                    };
                                    newReservation.Segments.Add(segObj);
                                }
                            }
                        }
                        catch { }
                    }

                    _dbContext.FlightReservations.Add(newReservation);
                    await _dbContext.SaveChangesAsync();

                    if (request.Passengers != null && request.Passengers.Any())
                    {
                        var reservationPassengers = new List<FlightReservationPassenger>();
                        var responsePassengers = new List<JsonElement>();
                        if (resp.TryGetProperty("FlightItinerary", out var itineraryNode) && 
                            itineraryNode.TryGetProperty("Passenger", out var passArray) && 
                            passArray.ValueKind == JsonValueKind.Array)
                        {
                            responsePassengers = passArray.EnumerateArray().ToList();
                        }

                        for (int i = 0; i < request.Passengers.Count; i++)
                        {
                            var p = request.Passengers[i];
                            var passObj = new FlightReservationPassenger
                            {
                                FlightReservationId = newReservation.Id,
                                FullName = $"{p.FirstName} {p.LastName}",
                                FirstName = p.FirstName,
                                LastName = p.LastName,
                                Title = p.Title,
                                PassportNo = p.PassportNo,
                                Nationality = p.CountryName,
                                Email = p.Email,
                                ContactNo = p.ContactNo,
                                DateOfBirth = DateTime.TryParse(p.DateOfBirth, out var dob2) ? dob2 : null,
                                PassengerType = p.PaxType == 1 ? "Adult" : p.PaxType == 2 ? "Child" : "Infant",
                                Gender = p.Gender == "1" ? "Male" : "Female",
                                SeatNumber = p.Seat != null && p.Seat.Any() ? string.Join(", ", p.Seat.Select(s => s.SeatNumber)) : null
                            };

                            if (i < responsePassengers.Count)
                            {
                                var matchedPax = responsePassengers.FirstOrDefault(r => 
                                    r.TryGetProperty("FirstName", out var fn) && fn.ToString()?.Equals(p.FirstName, StringComparison.OrdinalIgnoreCase) == true &&
                                    r.TryGetProperty("LastName", out var ln) && ln.ToString()?.Equals(p.LastName, StringComparison.OrdinalIgnoreCase) == true
                                );
                                
                                var rPax = matchedPax.ValueKind != JsonValueKind.Undefined ? matchedPax : responsePassengers[i];
                                passObj.PaxId = rPax.TryGetProperty("PaxId", out var paxIdNode) && paxIdNode.ValueKind == JsonValueKind.Number ? paxIdNode.GetInt32() : null;
                                
                                if (rPax.TryGetProperty("Ticket", out var tktNode))
                                {
                                    passObj.TicketId = tktNode.TryGetProperty("TicketId", out var tId) ? tId.ToString() : null;
                                    passObj.TicketNumber = tktNode.TryGetProperty("TicketNumber", out var tNum) ? tNum.ToString() : null;
                                }
                            }

                            reservationPassengers.Add(passObj);
                        }
                        _dbContext.FlightReservationPassengers.AddRange(reservationPassengers);
                        await _dbContext.SaveChangesAsync();
                    }
                }
            } // Close if (isSuccess)

            return Ok(outNode ?? (object)doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting TicketGDS.");
                return StatusCode(500, new { message = "Failed to get TicketGDS.", error = ex.Message });
            }
        }


        [Authorize]
        [HttpPost("SendChangeRequest")]
        public async Task<IActionResult> SendChangeRequest([FromBody] FlightSendChangeProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new SendChangeRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    BookingId = proxyRequest.BookingId,
                    RequestType = proxyRequest.RequestType,
                    CancellationType = proxyRequest.CancellationType,
                    Remarks = proxyRequest.Remarks,
                    Sectors = proxyRequest.Sectors,
                    SrdvType = proxyRequest.SrdvType,
                    SrdvIndex = proxyRequest.SrdvIndex,
                    TicketData = proxyRequest.TicketData,
                    PNR = proxyRequest.PNR
                };
                var responseRaw = await _srdvFlightService.SendChangeRequestRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;
                
                var reservation = await _dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(r => r.SrdvBookingId == request.BookingId);
                if (reservation == null && !string.IsNullOrEmpty(request.PNR))
                {
                    reservation = await _dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(r => r.Pnr == request.PNR);
                }
                
                if (reservation != null)
                {
                    var isSuccess = false;
                    JsonElement resp = root;
                    if (root.TryGetProperty("Response", out var responseNode))
                    {
                        resp = responseNode;
                    }
                    else if (root.TryGetProperty("Results", out var resultsNode))
                    {
                        resp = resultsNode;
                    }
                    
                    if (resp.TryGetProperty("ResponseStatus", out var status))
                    {
                        if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                        if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                    }
                    
                    var errSource = root.TryGetProperty("Error", out var rootErr) ? root : resp;
                    if (errSource.TryGetProperty("Error", out var err) && err.TryGetProperty("ErrorCode", out var errCode))
                    {
                        if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                        if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                        if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;
                    }
                    
                    if (isSuccess)
                    {
                        string changeRequestId = "";
                        if (resp.TryGetProperty("TicketCRInfo", out var crInfo) && crInfo.ValueKind == JsonValueKind.Array && crInfo.GetArrayLength() > 0)
                        {
                            var firstCR = crInfo[0];
                            if (firstCR.TryGetProperty("ChangeRequestId", out var crIdNode))
                                changeRequestId = crIdNode.ToString();
                        }
                        
                        var isPartial = false;
                        var reqSectors = request.Sectors != null && request.Sectors.Any() ? System.Text.Json.JsonSerializer.Serialize(request.Sectors) : null;
                        var reqTickets = request.TicketData != null && request.TicketData.Any() ? System.Text.Json.JsonSerializer.Serialize(request.TicketData) : null;

                        if (request.Sectors != null && request.Sectors.Any() && request.Sectors.Count < reservation.Segments.Count)
                            isPartial = true;
                        if (request.TicketData != null && request.TicketData.Any() && request.TicketData.Count < reservation.SeatsBooked) // or passengers count
                            isPartial = true;

                        var cancelReq = new FlightCancellationRequest
                        {
                            FlightReservationId = reservation.Id,
                            RequestDateUtc = DateTime.UtcNow,
                            CancellationStatus = "Pending",
                            CustomerRefundStatus = "Pending",
                            AdminRefundStatus = "Pending",
                            SrdvChangeRequestId = changeRequestId,
                            SrdvBookingId = request.BookingId,
                            SrdvType = request.SrdvType,
                            SrdvIndex = request.SrdvIndex,
                            CustomerRemark = request.Remarks,
                            IsPartialCancellation = isPartial,
                            CancelledSectorsJson = reqSectors,
                            CancelledPassengersJson = reqTickets
                        };
                        
                        reservation.Status = isPartial ? "Partial Cancellation Requested" : "Cancellation Requested";
                        
                        _dbContext.FlightCancellationRequests.Add(cancelReq);
                        
                        // Create BookingCancellation as the single financial ledger
                        var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.UserId == reservation.UserId && p.BookingReferenceId == reservation.Id && p.BookingType == "Flight");
                        var bookingCancellation = new BookingCancellation
                        {
                            BookingReference = reservation.BookingReference,
                            BookingType = "Flight",
                            PaymentId = payment?.Id ?? 0,
                            UserId = reservation.UserId,
                            OriginalCustomerPaid = payment?.FinalPayableAmount ?? reservation.CustomerFareInr,
                            SupplierAmount = reservation.NetFareInr,
                            MarkupAmount = reservation.MarkupAmount,
                            DiscountAmount = reservation.DiscountAmountInr + reservation.PromotionDiscount + reservation.CouponDiscount,
                            ConvenienceFee = payment?.ConvenienceFee ?? 0m,
                            SrdvChangeRequestId = changeRequestId,
                            Status = "Pending",
                            SrdvStatus = "Pending",
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.BookingCancellations.Add(bookingCancellation);
                        
                        await _dbContext.SaveChangesAsync();
                    }
                }

                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending change request.");
                return StatusCode(500, new { message = "Failed to send change request.", error = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("GetCancelStatus")]
        public async Task<IActionResult> GetCancelStatus([FromBody] FlightGetCancelStatusProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new GetCancelStatusRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    ChangeRequestId = proxyRequest.ChangeRequestId
                };
                var responseRaw = await _srdvFlightService.GetCancelStatusRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;
                
                var isSuccess = false;
                JsonElement resp = root;
                if (root.TryGetProperty("Response", out var responseNode))
                {
                    resp = responseNode;
                }
                else if (root.TryGetProperty("Results", out var resultsNode))
                {
                    resp = resultsNode;
                }
                
                if (resp.TryGetProperty("ResponseStatus", out var status))
                {
                    if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                    if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                }
                
                var errSource = root.TryGetProperty("Error", out var rootErr) ? root : resp;
                if (errSource.TryGetProperty("Error", out var err) && err.TryGetProperty("ErrorCode", out var errCode))
                {
                    if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                    if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;
                }
                if (isSuccess)
                {
                    // Note: Actual refund calculation, persistence, and Cashfree refund initiation
                    // are now strictly handled by the FulfillmentRecoveryWorker to ensure idempotency
                    // and a single source of truth for financial ledgers.
                }

                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cancel status.");
                return StatusCode(500, new { message = "Failed to get cancel status.", error = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("GetCancellationCharges")]
        public async Task<IActionResult> GetCancellationCharges([FromBody] FlightGetCancellationChargesProxyRequestDto proxyRequest)
        {
            try
            {
                var request = new GetCancellationChargesRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    RequestType = proxyRequest.RequestType,
                    TraceId = proxyRequest.TraceId
                };
                var responseRaw = await _srdvFlightService.GetCancellationChargesRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cancellation charges.");
                return StatusCode(500, new { message = "Failed to get cancellation charges.", error = ex.Message });
            }
        }
        [HttpPost("GetApiBalanceCheck")]
        public async Task<IActionResult> GetApiBalanceCheck([FromBody] ApiBalanceRequestDto request)
        {
            try
            {
                var responseRaw = await _srdvFlightService.GetApiBalanceCheckRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API balance check.");
                return StatusCode(500, new { message = "Failed to get API balance check.", error = ex.Message });
            }
        }

        [HttpPost("GetApiBalanceLog")]
        public async Task<IActionResult> GetApiBalanceLog([FromBody] ApiBalanceRequestDto request)
        {
            try
            {
                var responseRaw = await _srdvFlightService.GetApiBalanceLogRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API balance log.");
                return StatusCode(500, new { message = "Failed to get API balance log.", error = ex.Message });
            }
        }

        [HttpPost("flight_callback")]
        [ProducesResponseType(typeof(SrdvBookingCallbackResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> BookingCallback([FromBody] FlightBookingCallbackProxyRequestDto proxyRequest)
        {
            var request = new SrdvBookingCallbackRequestDto
            {
                ClientId = _srdvSettings.ClientId,
                UserName = _srdvSettings.UserName,
                Password = _srdvSettings.Password,
                EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                TraceId = proxyRequest.TraceId,
                BookingId = proxyRequest.BookingId,
                PNR = proxyRequest.PNR,
                GdsPNR = proxyRequest.GdsPNR,
                Status = proxyRequest.Status,
                Remark = proxyRequest.Remark,
                Passengers = proxyRequest.Passengers
            };
            try
            {
                _logger.LogInformation("Received SRDV Booking Update Callback for BookingId: {BookingId}", request.BookingId);

                // Security Check: Verify credentials against settings
                var apiTokenHeader = Request.Headers["Api-Token"].FirstOrDefault();
                if (request.ClientId != _srdvSettings.ClientId || 
                    request.UserName != _srdvSettings.UserName || 
                    request.Password != _srdvSettings.Password ||
                    apiTokenHeader != _srdvSettings.ApiToken)
                {
                    _logger.LogWarning("SRDV Booking Callback failed authentication for BookingId: {BookingId}", request.BookingId);
                    return Ok(new SrdvBookingCallbackResponseDto { Error = new SrdvCallbackErrorDto { ErrorCode = "1", ErrorMessage = "Unauthorized" } });
                }

                if (string.IsNullOrEmpty(request.BookingId) && string.IsNullOrEmpty(request.PNR))
                {
                    _logger.LogWarning("Received callback with missing PNR and BookingId.");
                    return Ok(new SrdvBookingCallbackResponseDto { Error = new SrdvCallbackErrorDto { ErrorCode = "2", ErrorMessage = "Missing PNR and BookingId" } });
                }

                var reservationQuery = _dbContext.FlightReservations.AsQueryable();

                if (!string.IsNullOrEmpty(request.BookingId))
                {
                    reservationQuery = reservationQuery.Where(r => r.SrdvBookingId == request.BookingId);
                }
                else if (!string.IsNullOrEmpty(request.PNR))
                {
                    reservationQuery = reservationQuery.Where(r => r.Pnr == request.PNR);
                }

                var reservation = await reservationQuery.FirstOrDefaultAsync();

                if (reservation != null)
                {
                    reservation.SrdvCallbackResponseJson = JsonSerializer.Serialize(request);
                    reservation.CallbackReceivedAtUtc = DateTime.UtcNow;

                    if (!string.IsNullOrEmpty(request.Status))
                    {
                        reservation.TicketStatus = request.Status;

                        // Map SRDV callback status to our reservation status
                        var srdvStatus = request.Status.ToLower();
                        if (srdvStatus == "success" || srdvStatus == "ticketed")
                        {
                            reservation.Status = "Booked";
                        }
                        else if (srdvStatus == "failed" || srdvStatus == "aborted")
                        {
                            reservation.Status = "Failed";
                        }
                    }

                    if (!string.IsNullOrEmpty(request.PNR) && string.IsNullOrEmpty(reservation.Pnr))
                    {
                        reservation.Pnr = request.PNR;
                    }

                    // Sync Passenger Ticket Numbers
                    if (request.Passengers != null && request.Passengers.Any())
                    {
                        var reservationPassengers = await _dbContext.FlightReservationPassengers
                            .Where(p => p.FlightReservationId == reservation.Id)
                            .ToListAsync();

                        foreach (var incPax in request.Passengers)
                        {
                            if (!string.IsNullOrEmpty(incPax.TicketNumber))
                            {
                                var dbPax = reservationPassengers.FirstOrDefault(p => 
                                    string.Equals(p.FirstName, incPax.FirstName, StringComparison.OrdinalIgnoreCase) && 
                                    string.Equals(p.LastName, incPax.LastName, StringComparison.OrdinalIgnoreCase));
                                
                                if (dbPax != null)
                                {
                                    dbPax.TicketNumber = incPax.TicketNumber;
                                }
                            }
                        }
                    }

                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Successfully updated reservation {ReservationId} with callback data.", reservation.Id);

                    // Credit wallet back if booking failed (we debited on Pending)
                    if (reservation.Status == "Failed" && reservation.SupplierTotalFare > 0)
                    {
                        if (int.TryParse(reservation.UserId, out var agentId) && agentId > 0)
                        {
                            var user = await _dbContext.Users.FindAsync(agentId);
                            if (user != null && user.Role == AuthRoles.Agent)
                            {
                                await _walletService.CreditWalletForRefundAsync(agentId,
                                    reservation.SupplierTotalFare,
                                    reservation.BookingReference,
                                    "Flight",
                                    $"Refund - Failed Flight Booking PNR {reservation.Pnr}");
                            }
                        }
                    }

                    // Dispatch final email if tickets are issued
                    if (reservation.TicketStatus == "Ticketed" || request.Status?.ToLower() == "success")
                    {
                        try
                        {
                            global::User? agentInfo = null;
                            if (int.TryParse(reservation.UserId, out var aId) && aId > 0)
                            {
                                agentInfo = await _dbContext.Users.FindAsync(aId);
                            }
                            var emailReq = new SendFlightTicketEmailRequest
                            {
                                ToEmail = string.IsNullOrEmpty(reservation.PassengerEmail) ? (agentInfo?.Email ?? "") : reservation.PassengerEmail,
                                PassengerName = reservation.PassengerName,
                                BookingReference = reservation.BookingReference,
                                Airline = reservation.Airline,
                                Origin = reservation.FromCity,
                                Destination = reservation.ToCity,
                                DepartureTime = reservation.DepartureTime,
                                ArrivalTime = reservation.ArrivalTime,
                                Pnr = reservation.Pnr,
                                Price = reservation.TotalPriceInr,
                                Currency = "INR",
                                NonRefundable = reservation.NonRefundable,
                                CancellationCharges = reservation.CancellationCharges,
                                PartialSegmentCancellation = reservation.PartialSegmentCancellation,
                                AgentCompanyName = agentInfo?.CompanyName,
                                AgentLogoUrl = agentInfo?.AgentLogoUrl,
                                Passengers = await _dbContext.FlightReservationPassengers
                                                .Where(p => p.FlightReservationId == reservation.Id)
                                                .Select(p => new FlightPassengerTicketDto {
                                                    FullName = p.FullName,
                                                    PassengerType = p.PassengerType,
                                                    Gender = p.Gender,
                                                    SeatNumber = p.SeatNumber,
                                                    TicketNumber = p.TicketNumber
                                                }).ToListAsync(),
                                Segments = reservation.Segments.Select(s => new FlightTicketSegmentDto {
                                    Airline = s.Airline,
                                    FlightNumber = s.FlightNumber,
                                    FromCity = s.FromCity,
                                    ToCity = s.ToCity,
                                    DepartureTime = s.DepartureTime,
                                    ArrivalTime = s.ArrivalTime,
                                    Pnr = s.Pnr
                                }).ToList()
                            };
                            var backgroundJobQueue = HttpContext.RequestServices.GetRequiredService<PickNBook.Api.Services.IBackgroundJobQueue>();
                            backgroundJobQueue.QueueBackgroundWorkItem(async (sp, ct) =>
                            {
                                var scopedEmailService = sp.GetRequiredService<ITicketEmailService>();
                                await scopedEmailService.SendFlightTicketAsync(emailReq);
                            });
                            _logger.LogInformation("Successfully dispatched final ticket email via callback for BookingReference: {BookingRef}", reservation.BookingReference);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send final ticket email via callback for Booking {BookingReference}", reservation.BookingReference);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Received callback for PNR {PNR} / BookingId {BookingId} but no matching reservation was found.", request.PNR, request.BookingId);
                    // Still return success to SRDV so they don't retry unnecessarily if the record doesn't exist on our end.
                }

                // Exactly match the required success response structure
                return Ok(new SrdvBookingCallbackResponseDto { Error = new SrdvCallbackErrorDto { ErrorCode = "0", ErrorMessage = "" } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SRDV Booking Callback.");
                return Ok(new SrdvBookingCallbackResponseDto { Error = new SrdvCallbackErrorDto { ErrorCode = "500", ErrorMessage = "Internal Server Error" } });
            }
        }


        [Authorize]
        [HttpGet("my-bookings")]
        public async Task<IActionResult> MyBookings()
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "User is not authenticated." });
                }

                var bookings = await _dbContext.FlightReservations
                    .Where(x => x.UserId == userId)
                    .OrderByDescending(x => x.Id)
                    .ToListAsync();

                var result = new List<MyFlightBookingResponseDto>();

                foreach (var booking in bookings)
                {
                    var passengers = await _dbContext.FlightReservationPassengers
                        .Where(p => p.FlightReservationId == booking.Id)
                        .ToListAsync();

                    var responseDto = new MyFlightBookingResponseDto
                    {
                        BookingReference = booking.BookingReference,
                        FromCity = booking.FromCity,
                        ToCity = booking.ToCity,
                        DepartureTime = booking.DepartureTime,
                        Status = booking.Status,
                        TotalFare = booking.TotalPriceInr,

                        // Fields for Cancellation
                        TraceId = booking.TraceId,
                        BookingId = booking.SrdvBookingId,
                        PNR = booking.Pnr,
                        SrdvType = booking.SrdvType,
                        SrdvIndex = booking.SrdvIndex,

                        Passengers = passengers.Select(p => new MyFlightPassengerDto
                        {
                            FullName = p.FullName,
                            SeatNumber = p.SeatNumber,
                            TicketId = p.TicketId
                        }).ToList()
                    };

                    result.Add(responseDto);
                }

                return Ok(new { success = true, tickets = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve flight bookings for user {UserId}", User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { message = "Failed to retrieve flight bookings history." });
            }
        }

        private IActionResult ValidatePassengersPassport(List<LCCPassengerDto> passengers)
        {
            if (passengers == null || !passengers.Any()) return null;
            
            var passportRegex = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9]{6,9}$");
            foreach (var p in passengers)
            {
                if (!string.IsNullOrWhiteSpace(p.PassportNo))
                {
                    if (!passportRegex.IsMatch(p.PassportNo))
                    {
                        return BadRequest(new { message = $"Invalid passport number format for passenger {p.FirstName} {p.LastName}. Passport number must be 6 to 9 alphanumeric characters." });
                    }
                }
            }
            return null;
        }
    }
}




















