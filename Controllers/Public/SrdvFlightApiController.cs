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
using PickNBook.Api.Filters;
using PickNBook.Api.Helpers;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Services.Implementations;

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
        private readonly PickNBook.Api.Services.Interfaces.IWalletService _userWalletService;
        private readonly SrdvSettings _srdvSettings;
        private readonly ILogger<SrdvFlightApiController> _logger;
        private readonly ICancellationRefundCalculator _refundCalculator;
        private readonly IAirlineLookupService _airlineLookup;

        public SrdvFlightApiController(
            ISrdvFlightService srdvFlightService, 
            IFlightPricingService pricingService,
            AppDbContext dbContext,
            ITicketEmailService ticketEmailService,
            IAgentWalletService walletService,
            PickNBook.Api.Services.Interfaces.IWalletService userWalletService,
            IOptions<SrdvSettings> srdvSettings,
            ICancellationRefundCalculator refundCalculator,
            IAirlineLookupService airlineLookup,
            ILogger<SrdvFlightApiController> logger)
        {
            _srdvFlightService = srdvFlightService;
            _pricingService = pricingService;
            _dbContext = dbContext;
            _ticketEmailService = ticketEmailService;
            _walletService = walletService;
            _userWalletService = userWalletService;
            _srdvSettings = srdvSettings.Value;
            _refundCalculator = refundCalculator;
            _airlineLookup = airlineLookup;
            _logger = logger;
        }

        [HttpGet("Airlines")]
        [ResponseCache(Duration = 86400)]
        public async Task<IActionResult> GetAirlines(CancellationToken cancellationToken)
        {
            var airlines = await _airlineLookup.GetAllAirlinesAsync(cancellationToken);
            return Ok(airlines);
        }

        [HttpPost("Search")]
        public async Task<IActionResult> Search([FromBody] FlightSearchProxyRequestDto proxyRequest)
        {
            try
            {
                if (proxyRequest == null)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "Request body cannot be empty." });
                }

                if (proxyRequest.AdultCount < 1 || proxyRequest.AdultCount > 9)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "AdultCount must be between 1 and 9." });
                }

                if (proxyRequest.ChildCount < 0 || proxyRequest.ChildCount > 8)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "ChildCount must be between 0 and 8." });
                }

                if (proxyRequest.InfantCount < 0 || proxyRequest.InfantCount > 9)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "InfantCount must be between 0 and 9." });
                }

                if (proxyRequest.InfantCount > proxyRequest.AdultCount)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "InfantCount cannot exceed AdultCount." });
                }

                if (proxyRequest.Segments == null || proxyRequest.Segments.Count == 0)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "At least one flight segment is required." });
                }

                foreach (var seg in proxyRequest.Segments)
                {
                    if (string.IsNullOrWhiteSpace(seg.Origin) || seg.Origin.Trim().Length != 3)
                    {
                        return BadRequest(new { ErrorCode = 1, ErrorMessage = $"Invalid segment origin '{seg.Origin}'. Must be a 3-character airport/city code." });
                    }
                    if (string.IsNullOrWhiteSpace(seg.Destination) || seg.Destination.Trim().Length != 3)
                    {
                        return BadRequest(new { ErrorCode = 1, ErrorMessage = $"Invalid segment destination '{seg.Destination}'. Must be a 3-character airport/city code." });
                    }
                    seg.Origin = seg.Origin.Trim().ToUpperInvariant();
                    seg.Destination = seg.Destination.Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(seg.FlightCabinClass))
                    {
                        seg.FlightCabinClass = "1";
                    }
                }

                if (!string.IsNullOrWhiteSpace(proxyRequest.CurrencyCode) && proxyRequest.CurrencyCode.Trim().Length != 3)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "CurrencyCode must be a 3-character ISO currency code (e.g. INR)." });
                }

                var request = new AirSearchRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    AdultCount = proxyRequest.AdultCount,
                    ChildCount = proxyRequest.ChildCount,
                    InfantCount = proxyRequest.InfantCount,
                    JourneyType = string.IsNullOrWhiteSpace(proxyRequest.JourneyType) ? "1" : proxyRequest.JourneyType.Trim(),
                    CurrencyCode = string.IsNullOrWhiteSpace(proxyRequest.CurrencyCode) ? "INR" : proxyRequest.CurrencyCode.Trim().ToUpperInvariant(),
                    FareType = string.IsNullOrWhiteSpace(proxyRequest.FareType) ? "1" : proxyRequest.FareType.Trim(),
                    DirectFlight = proxyRequest.DirectFlight,
                    Segments = proxyRequest.Segments
                };
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
                
                var expandedRequests = ExpandMetroClusterRequests(request);
                string responseRaw;
                if (expandedRequests.Count <= 1)
                {
                    responseRaw = await _srdvFlightService.SearchFlightsRawAsync(request);
                }
                else
                {
                    var flightTasks = expandedRequests.Select(r => _srdvFlightService.SearchFlightsRawAsync(r)).ToList();
                    var rawResponses = await Task.WhenAll(flightTasks);
                    responseRaw = MergeFlightSearchResponses(rawResponses);
                }

                var jsonNode = JsonNode.Parse(responseRaw);
                var responseObj = jsonNode; // The root is the response object
                
                var errorCode = responseObj?["Error"]?["ErrorCode"]?.ToString();
                
                var jType = (request.JourneyType ?? "1").Trim();
                var requestTripType = jType == "2" ? TripType.RoundTrip : (jType == "3" ? TripType.MultiCity : TripType.OneWay);
                
                _logger.LogInformation("Search Flight API triggered. SRDV ErrorCode: {ErrorCode}", errorCode);
                
                if (errorCode == "0") // SRDV V8: ErrorCode 0 = success, any other value = error
                {
                    try 
                    {
                        var traceId = responseObj?["TraceId"]?.ToString();
                        
                        // Collect all unique airport codes in the search segments
                        var airportCodes = (request.Segments ?? new List<AirSearchSegmentDto>())
                            .SelectMany(s => new[] { s.Origin, s.Destination })
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Select(c => c.Trim().ToUpperInvariant())
                            .Distinct()
                            .ToList();

                        var airportList = await _dbContext.FlightAirports
                            .AsNoTracking()
                            .Where(a => airportCodes.Contains(a.AirportCode))
                            .ToListAsync();

                        var airportDict = airportList
                            .GroupBy(a => a.AirportCode.ToUpperInvariant())
                            .ToDictionary(
                                g => g.Key,
                                g => string.IsNullOrWhiteSpace(g.First().CityName) ? g.First().AirportName : g.First().CityName
                            );

                        string fromCity = "";
                        string toCity = "";
                        DateOnly? departDate = null;
                        DateOnly? returnDate = null;
                        string routeSummary = "";

                        var firstSeg = request.Segments?.FirstOrDefault();
                        var lastSeg = request.Segments?.LastOrDefault();

                        if (requestTripType == TripType.RoundTrip)
                        {
                            fromCity = firstSeg?.Origin ?? "";
                            toCity = firstSeg?.Destination ?? "";
                            departDate = firstSeg != null ? DateOnly.FromDateTime(firstSeg.PreferredDepartureTime) : null;
                            var returnSeg = request.Segments != null && request.Segments.Count > 1 ? request.Segments[1] : null;
                            returnDate = returnSeg != null ? DateOnly.FromDateTime(returnSeg.PreferredDepartureTime) : null;
                            routeSummary = $"{fromCity} ⇄ {toCity}";
                        }
                        else if (requestTripType == TripType.MultiCity)
                        {
                            fromCity = firstSeg?.Origin ?? "";
                            toCity = lastSeg?.Destination ?? "";
                            departDate = firstSeg != null ? DateOnly.FromDateTime(firstSeg.PreferredDepartureTime) : null;
                            returnDate = null;
                            var routePoints = new List<string>();
                            if (request.Segments != null)
                            {
                                foreach (var seg in request.Segments)
                                {
                                    if (routePoints.Count == 0 || routePoints.Last() != seg.Origin)
                                    {
                                        routePoints.Add(seg.Origin);
                                    }
                                    routePoints.Add(seg.Destination);
                                }
                            }
                            routeSummary = string.Join(" ➔ ", routePoints);
                        }
                        else // OneWay
                        {
                            fromCity = firstSeg?.Origin ?? "";
                            toCity = firstSeg?.Destination ?? "";
                            departDate = firstSeg != null ? DateOnly.FromDateTime(firstSeg.PreferredDepartureTime) : null;
                            returnDate = null;
                            routeSummary = $"{fromCity} ➔ {toCity}";
                        }

                        var fromCityName = airportDict.TryGetValue(fromCity, out var fcn) ? fcn : fromCity;
                        var toCityName = airportDict.TryGetValue(toCity, out var tcn) ? tcn : toCity;

                        var segmentSnapshots = (request.Segments ?? new List<AirSearchSegmentDto>())
                            .Select((s, index) => new
                            {
                                SegmentIndex = index + 1,
                                Origin = s.Origin,
                                OriginCity = airportDict.TryGetValue(s.Origin, out var oc) ? oc : s.Origin,
                                Destination = s.Destination,
                                DestinationCity = airportDict.TryGetValue(s.Destination, out var dc) ? dc : s.Destination,
                                DepartureDate = s.PreferredDepartureTime.ToString("yyyy-MM-dd"),
                                FlightCabinClass = s.FlightCabinClass
                            }).ToList();

                        var segmentsJson = JsonSerializer.Serialize(segmentSnapshots);

                        var searchLog = new FlightSearchLog
                        {
                            SearchedAtUtc = DateTime.UtcNow,
                            FromCity = fromCity,
                            FromCityName = fromCityName,
                            ToCity = toCity,
                            ToCityName = toCityName,
                            DepartDate = departDate,
                            ReturnDate = returnDate,
                            Adults = request.AdultCount,
                            Children = request.ChildCount,
                            Infants = request.InfantCount,
                            TripType = jType,
                            RouteSummary = routeSummary,
                            SegmentsJson = segmentsJson,
                            UserId = string.IsNullOrEmpty(userId) ? null : userId,
                            IsGuest = string.IsNullOrEmpty(userId),
                            UserOrGuestId = userId,
                            TraceId = traceId,
                            EndUserIp = request.EndUserIp
                        };
                        
                        _dbContext.FlightSearchLogs.Add(searchLog);
                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation("Successfully inserted flight search log to Database. TraceId: {TraceId}, Route: {RouteSummary}", traceId, routeSummary);
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
                        errObj["ErrorMessage"] = PickNBook.Api.Infrastructure.Helpers.SrdvErrorHelper.GetErrorMessage(errorCode, errObj["ErrorMessage"]?.ToString());
                    }
                }

                await EnrichFlightResultsAsync(responseObj, requestTripType, request.AdultCount + request.ChildCount + request.InfantCount, userId);

                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching flights.");
                return StatusCode(500, new { message = "Failed to search flights.", error = ex.Message });
            }
        }

        private static List<AirSearchRequestDto> ExpandMetroClusterRequests(AirSearchRequestDto baseRequest)
        {
            var requests = new List<AirSearchRequestDto> { baseRequest };

            for (int segIdx = 0; segIdx < baseRequest.Segments.Count; segIdx++)
            {
                var seg = baseRequest.Segments[segIdx];
                string[]? origAirports = null;
                string[]? destAirports = null;

                if (PlacesService.KnownMetroAirportClusters.TryGetValue(seg.Origin, out var origCluster) && origCluster.AirportCodes.Length > 1)
                {
                    origAirports = origCluster.AirportCodes;
                }

                if (PlacesService.KnownMetroAirportClusters.TryGetValue(seg.Destination, out var destCluster) && destCluster.AirportCodes.Length > 1)
                {
                    destAirports = destCluster.AirportCodes;
                }

                if (destAirports != null)
                {
                    var nextRequests = new List<AirSearchRequestDto>();
                    foreach (var r in requests)
                    {
                        foreach (var airportCode in destAirports)
                        {
                            var clonedSegments = r.Segments.Select(s => new AirSearchSegmentDto
                            {
                                Origin = s.Origin,
                                Destination = s.Destination,
                                PreferredDepartureTime = s.PreferredDepartureTime,
                                PreferredArrivalTime = s.PreferredArrivalTime,
                                FlightCabinClass = s.FlightCabinClass
                            }).ToList();

                            clonedSegments[segIdx].Destination = airportCode;
                            if (r.Segments.Count == 2 && segIdx == 0 && r.Segments[1].Origin.Equals(seg.Destination, StringComparison.OrdinalIgnoreCase))
                            {
                                clonedSegments[1].Origin = airportCode;
                            }

                            nextRequests.Add(new AirSearchRequestDto
                            {
                                EndUserIp = r.EndUserIp,
                                AdultCount = r.AdultCount,
                                ChildCount = r.ChildCount,
                                InfantCount = r.InfantCount,
                                JourneyType = r.JourneyType,
                                CurrencyCode = r.CurrencyCode,
                                FareType = r.FareType,
                                DirectFlight = r.DirectFlight,
                                Segments = clonedSegments
                            });
                        }
                    }
                    requests = nextRequests;
                }

                if (origAirports != null)
                {
                    var nextRequests = new List<AirSearchRequestDto>();
                    foreach (var r in requests)
                    {
                        foreach (var airportCode in origAirports)
                        {
                            var clonedSegments = r.Segments.Select(s => new AirSearchSegmentDto
                            {
                                Origin = s.Origin,
                                Destination = s.Destination,
                                PreferredDepartureTime = s.PreferredDepartureTime,
                                PreferredArrivalTime = s.PreferredArrivalTime,
                                FlightCabinClass = s.FlightCabinClass
                            }).ToList();

                            clonedSegments[segIdx].Origin = airportCode;
                            if (r.Segments.Count == 2 && segIdx == 0 && r.Segments[1].Destination.Equals(seg.Origin, StringComparison.OrdinalIgnoreCase))
                            {
                                clonedSegments[1].Destination = airportCode;
                            }

                            nextRequests.Add(new AirSearchRequestDto
                            {
                                EndUserIp = r.EndUserIp,
                                AdultCount = r.AdultCount,
                                ChildCount = r.ChildCount,
                                InfantCount = r.InfantCount,
                                JourneyType = r.JourneyType,
                                CurrencyCode = r.CurrencyCode,
                                FareType = r.FareType,
                                DirectFlight = r.DirectFlight,
                                Segments = clonedSegments
                            });
                        }
                    }
                    requests = nextRequests;
                }
            }

            return requests;
        }

        private static string MergeFlightSearchResponses(string[] responses)
        {
            if (responses == null || responses.Length == 0) return "{}";
            if (responses.Length == 1) return responses[0];

            JsonNode? primaryRoot = null;
            var seenResultIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in responses)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    var node = JsonNode.Parse(raw);
                    if (node == null) continue;

                    var errorCode = node["Error"]?["ErrorCode"]?.ToString();
                    if (errorCode == "0")
                    {
                        if (primaryRoot == null)
                        {
                            primaryRoot = node;
                            var resultsArr = primaryRoot["Results"]?.AsArray();
                            if (resultsArr != null && resultsArr.Count > 0 && resultsArr[0] is JsonArray firstLegFlights)
                            {
                                foreach (var f in firstLegFlights)
                                {
                                    var rIndex = f?["ResultIndex"]?.ToString();
                                    if (!string.IsNullOrEmpty(rIndex)) seenResultIndexes.Add(rIndex);
                                }
                            }
                        }
                        else
                        {
                            var primaryResults = primaryRoot["Results"]?.AsArray();
                            var otherResults = node["Results"]?.AsArray();
                            if (primaryResults != null && otherResults != null)
                            {
                                for (int legIdx = 0; legIdx < Math.Min(primaryResults.Count, otherResults.Count); legIdx++)
                                {
                                    if (primaryResults[legIdx] is JsonArray primaryLeg && otherResults[legIdx] is JsonArray otherLeg)
                                    {
                                        foreach (var flight in otherLeg)
                                        {
                                            if (flight == null) continue;
                                            var rIndex = flight["ResultIndex"]?.ToString();
                                            if (string.IsNullOrEmpty(rIndex) || seenResultIndexes.Add(rIndex))
                                            {
                                                primaryLeg.Add(JsonNode.Parse(flight.ToJsonString())!);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore partial parse failures
                }
            }

            return primaryRoot?.ToJsonString() ?? responses.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) ?? responses[0];
        }

        [HttpPost("RecheckSearch")]
        public async Task<IActionResult> RecheckSearch([FromBody] FlightRecheckSearchProxyRequestDto proxyRequest)
        {
            try
            {
                if (proxyRequest == null || proxyRequest.TraceId <= 0)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "A valid positive TraceId is required." });
                }

                var request = new AirRecheckSearchRequestDto
                {
                    TraceId = proxyRequest.TraceId
                };

                var responseRaw = await _srdvFlightService.RecheckSearchRawAsync(request);
                var jsonNode = JsonNode.Parse(responseRaw);
                var responseObj = jsonNode;

                var errorCode = responseObj?["Error"]?["ErrorCode"]?.ToString();
                _logger.LogInformation("RecheckSearch API triggered for TraceId {TraceId}. ErrorCode: {ErrorCode}", proxyRequest.TraceId, errorCode);

                if (errorCode == "0")
                {
                    // Lookup original search context from DB if available to apply exact pricing rules
                    var traceIdStr = proxyRequest.TraceId.ToString();
                    var searchLog = await _dbContext.FlightSearchLogs.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.TraceId == traceIdStr);

                    var tripType = TripType.OneWay;
                    var passengerCount = 1;
                    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

                    if (searchLog != null)
                    {
                        var jType = (searchLog.TripType ?? "1").Trim();
                        tripType = jType == "2" ? TripType.RoundTrip : (jType == "3" ? TripType.MultiCity : TripType.OneWay);
                        passengerCount = Math.Max(1, searchLog.Adults + searchLog.Children + searchLog.Infants);
                        if (string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(searchLog.UserId))
                        {
                            userId = searchLog.UserId;
                        }
                    }

                    await EnrichFlightResultsAsync(responseObj, tripType, passengerCount, userId);
                }
                else
                {
                    if (responseObj?["Error"] is System.Text.Json.Nodes.JsonObject errObj)
                    {
                        errObj["ErrorMessage"] = PickNBook.Api.Infrastructure.Helpers.SrdvErrorHelper.GetErrorMessage(errorCode, errObj["ErrorMessage"]?.ToString());
                    }
                }

                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing RecheckSearch for TraceId {TraceId}", proxyRequest?.TraceId);
                return StatusCode(500, new { message = "Failed to recheck search.", error = ex.Message });
            }
        }

        private async Task EnrichFlightResultsAsync(
            JsonNode? responseObj, 
            TripType requestTripType, 
            int passengerCount, 
            string userId)
        {
            var resultsArr = responseObj?["Results"]?.AsArray();
            if (resultsArr == null) return;

            foreach (var flightList in resultsArr)
            {
                var flightsArr = flightList?.AsArray();
                if (flightsArr == null) continue;

                foreach (var result in flightsArr)
                {
                    if (result == null) continue;
                    
                    var segmentsArr = result["Segments"]?[0]?.AsArray();
                    if (segmentsArr == null || segmentsArr.Count == 0) continue;

                    // Match and enrich all segments in every leg with canonical display name from flight_airlines
                    var allSegmentLists = result["Segments"]?.AsArray();
                    if (allSegmentLists != null)
                    {
                        foreach (var segList in allSegmentLists)
                        {
                            var innerSegs = segList?.AsArray();
                            if (innerSegs == null) continue;
                            foreach (var seg in innerSegs)
                            {
                                if (seg?["Airline"] is JsonObject alObj)
                                {
                                    var rawCode = alObj["AirlineCode"]?.GetValue<string>() ?? "";
                                    var rawName = alObj["AirlineName"]?.GetValue<string>() ?? "";
                                    var canonicalName = _airlineLookup.GetAirlineName(rawCode, rawName);
                                    if (!string.IsNullOrEmpty(canonicalName))
                                    {
                                        alObj["AirlineName"] = canonicalName;
                                    }
                                    if (!string.IsNullOrEmpty(rawCode))
                                    {
                                        alObj["AirlineCode"] = rawCode.Trim().ToUpperInvariant();
                                    }
                                }
                            }
                        }
                    }
                    
                    var firstSegment = segmentsArr[0];
                    var lastSegment = segmentsArr[segmentsArr.Count - 1];
                    
                    var airlineCode = firstSegment?["Airline"]?["AirlineCode"]?.GetValue<string>() ?? "";
                    var rawAirlineName = firstSegment?["Airline"]?["AirlineName"]?.GetValue<string>() ?? "";
                    var airlineName = _airlineLookup.GetAirlineName(airlineCode, rawAirlineName);
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
                                    passengerCount: passengerCount,
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
                            passengerCount: passengerCount,
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
                if (proxyRequest == null)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "Request body cannot be empty." });
                }

                if (proxyRequest.TraceId <= 0)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "A valid positive TraceId is required." });
                }

                if (string.IsNullOrWhiteSpace(proxyRequest.ResultIndex) || proxyRequest.ResultIndex.Trim().Length < 3 || proxyRequest.ResultIndex.Trim().Length > 500)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "ResultIndex is required and must be between 3 and 500 characters." });
                }

                var request = new AirFareRuleRequestDto
                {
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex.Trim()
                };

                var responseRaw = await _srdvFlightService.GetFareRuleRawAsync(request);
                var jsonNode = JsonNode.Parse(responseRaw);
                var responseObj = jsonNode?["Response"] ?? jsonNode;
                var errorCode = responseObj?["Error"]?["ErrorCode"]?.ToString();

                if (errorCode != null && errorCode != "0")
                {
                    if (responseObj?["Error"] is System.Text.Json.Nodes.JsonObject errObj)
                    {
                        errObj["ErrorMessage"] = PickNBook.Api.Infrastructure.Helpers.SrdvErrorHelper.GetErrorMessage(errorCode, errObj["ErrorMessage"]?.ToString());
                    }
                }

                return Ok(jsonNode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting fare rule for TraceId {TraceId}, ResultIndex {ResultIndex}", proxyRequest?.TraceId, proxyRequest?.ResultIndex);
                return StatusCode(500, new { message = "Failed to get fare rule.", error = ex.Message });
            }
        }

        [HttpPost("FareQuote")]
        public async Task<IActionResult> FareQuote([FromBody] FlightFareQuoteProxyRequestDto proxyRequest)
        {
            try
            {
                if (proxyRequest == null)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "Request body cannot be empty." });
                }

                if (proxyRequest.TraceId <= 0)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "A valid positive TraceId is required." });
                }

                if (string.IsNullOrWhiteSpace(proxyRequest.ResultIndex) || proxyRequest.ResultIndex.Trim().Length < 3 || proxyRequest.ResultIndex.Trim().Length > 500)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "ResultIndex is required and must be between 3 and 500 characters." });
                }

                var request = new AirFareQuoteRequestDto
                {
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex.Trim()
                };
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

                var traceIdStr = proxyRequest.TraceId.ToString();
                var searchLog = await _dbContext.FlightSearchLogs.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TraceId == traceIdStr);

                TripType fqTripType = TripType.OneWay;
                if (!string.IsNullOrWhiteSpace(proxyRequest.JourneyType))
                {
                    var jType = proxyRequest.JourneyType.Trim();
                    fqTripType = jType == "2" ? TripType.RoundTrip : (jType == "3" ? TripType.MultiCity : TripType.OneWay);
                }
                else if (searchLog != null)
                {
                    var jType = (searchLog.TripType ?? "1").Trim();
                    fqTripType = jType == "2" ? TripType.RoundTrip : (jType == "3" ? TripType.MultiCity : TripType.OneWay);
                }
                else if (!string.IsNullOrEmpty(proxyRequest.ResultIndex) && proxyRequest.ResultIndex.Contains(","))
                {
                    fqTripType = TripType.RoundTrip;
                }

                int fqPaxCount = (proxyRequest.AdultCount ?? 0) + (proxyRequest.ChildCount ?? 0) + (proxyRequest.InfantCount ?? 0);
                if (fqPaxCount <= 0 && searchLog != null)
                {
                    fqPaxCount = searchLog.Adults + searchLog.Children + searchLog.Infants;
                }
                if (fqPaxCount <= 0)
                {
                    fqPaxCount = 1;
                }

                if (string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(searchLog?.UserId))
                {
                    userId = searchLog.UserId;
                }
                
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
                                couponCode: proxyRequest.CouponCode,
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

                            result["PickNBookAvailableOffers"] = JsonSerializer.SerializeToNode(Array.Empty<object>());
                        }
                    }
                }
                else
                {
                    if (responseObj?["Error"] is System.Text.Json.Nodes.JsonObject errObj)
                    {
                        errObj["ErrorMessage"] = PickNBook.Api.Infrastructure.Helpers.SrdvErrorHelper.GetErrorMessage(errorCode, errObj["ErrorMessage"]?.ToString());
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
        [HttpPost("/v8/SSR")]
        [HttpPost("/api/flight/v8/SSR")]
        public async Task<IActionResult> SSR([FromBody] FlightSSRProxyRequestDto proxyRequest)
        {
            if (proxyRequest == null || proxyRequest.TraceId <= 0 || string.IsNullOrWhiteSpace(proxyRequest.ResultIndex))
            {
                return BadRequest(new { message = "TraceId and ResultIndex are required." });
            }

            try
            {
                var request = new AirFareRuleRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType ?? string.Empty,
                    SrdvIndex = proxyRequest.SrdvIndex ?? string.Empty,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex
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
        [HttpPost("/v8/SeatMap")]
        [HttpPost("/api/flight/v8/SeatMap")]
        public async Task<IActionResult> SeatMap([FromBody] FlightSeatMapProxyRequestDto proxyRequest)
        {
            if (proxyRequest == null || proxyRequest.TraceId <= 0 || string.IsNullOrWhiteSpace(proxyRequest.ResultIndex))
            {
                return BadRequest(new { message = "TraceId and ResultIndex are required." });
            }

            try
            {
                var request = new AirFareRuleRequestDto
                {
                    EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                    SrdvType = proxyRequest.SrdvType ?? string.Empty,
                    SrdvIndex = proxyRequest.SrdvIndex ?? string.Empty,
                    TraceId = proxyRequest.TraceId,
                    ResultIndex = proxyRequest.ResultIndex
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


        [AllowAnonymous]
        [InjectClientIp]
        [HttpPost("/v8/TicketLCC")]
        [HttpPost("TicketLCC")]
        [HttpPost("/api/flight/v8/TicketLCC")]
        public async Task<IActionResult> TicketLCC([FromBody] FlightTicketLCCProxyRequestDto proxyRequest)
        {
            string? userIdStr = null;
            bool isPartnerApiCall = false;
            if (Request.Headers.TryGetValue("Api-Token", out var apiTokenHeader) && !string.IsNullOrWhiteSpace(apiTokenHeader))
            {
                isPartnerApiCall = true;
                userIdStr = "partner_api";
            }
            else if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
            }
            else
            {
                return Unauthorized(ValidationErrorDto.Create("Please login or provide a valid Api-Token header to book tickets.", "Authorization"));
            }

            if (proxyRequest == null)
            {
                return BadRequest(ValidationErrorDto.Create("Request body cannot be null.", "request"));
            }

            if (proxyRequest.TraceId <= 0)
            {
                return BadRequest(ValidationErrorDto.Create("TraceId is required and must be a positive integer.", "TraceId"));
            }

            var trimmedResultIndex = proxyRequest.ResultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedResultIndex) || trimmedResultIndex.Length < 3 || trimmedResultIndex.Length > 500)
            {
                return BadRequest(ValidationErrorDto.Create("ResultIndex is required and must be between 3 and 500 characters.", "ResultIndex"));
            }

            if (!string.IsNullOrWhiteSpace(proxyRequest.RefID) && proxyRequest.RefID.Trim().Length > 200)
            {
                return BadRequest(ValidationErrorDto.Create("RefID must not exceed 200 characters.", "RefID"));
            }

            if (proxyRequest.Passengers == null || proxyRequest.Passengers.Count < 1 || proxyRequest.Passengers.Count > 18)
            {
                return BadRequest(ValidationErrorDto.Create("Passengers count must be between 1 and 18.", "Passengers"));
            }

            // Check adult count
            int adultCount = proxyRequest.Passengers.Count(p => p.PaxType == 1);
            if (adultCount < 1)
            {
                return BadRequest(ValidationErrorDto.Create("At least one adult passenger (PaxType = 1) is required.", "Passengers"));
            }

            // Check infant count <= adult count
            int infantCount = proxyRequest.Passengers.Count(p => p.PaxType == 3);
            if (infantCount > adultCount)
            {
                return BadRequest(ValidationErrorDto.Create($"Number of infants ({infantCount}) cannot exceed number of adult passengers ({adultCount}).", "Passengers"));
            }

            // Ensure exactly one lead passenger
            int leadCount = proxyRequest.Passengers.Count(p => p.IsLeadPax);
            if (leadCount == 0)
            {
                var firstAdult = proxyRequest.Passengers.FirstOrDefault(p => p.PaxType == 1) ?? proxyRequest.Passengers[0];
                firstAdult.IsLeadPax = true;
            }
            else if (leadCount > 1)
            {
                bool firstFound = false;
                foreach (var pax in proxyRequest.Passengers)
                {
                    if (pax.IsLeadPax)
                    {
                        if (firstFound) pax.IsLeadPax = false;
                        else firstFound = true;
                    }
                }
            }

            var leadPax = proxyRequest.Passengers.First(p => p.IsLeadPax);

            // Lead Passenger Email validation
            var leadEmailVal = TravelValidationHelper.ValidateEmail(leadPax.Email, isRequired: true, "Lead passenger email");
            if (!leadEmailVal.IsValid)
            {
                return BadRequest(ValidationErrorDto.Create(leadEmailVal.ErrorMessage!, "Email", "INVALID_EMAIL"));
            }
            leadPax.Email = leadEmailVal.CleanedEmail;

            // Lead Passenger Mobile validation (strip +91, clean 10-digit)
            var leadMobileVal = TravelValidationHelper.ValidateMobileNumber(leadPax.ContactNo, isRequired: true, "Lead passenger mobile number");
            if (!leadMobileVal.IsValid)
            {
                return BadRequest(ValidationErrorDto.Create(leadMobileVal.ErrorMessage!, "ContactNo", "INVALID_PHONE"));
            }
            leadPax.ContactNo = leadMobileVal.CleanedPhone;

            DateTime departureDate = DateTime.UtcNow.Date;

            // Validate all passengers
            for (int i = 0; i < proxyRequest.Passengers.Count; i++)
            {
                var p = proxyRequest.Passengers[i];

                if (string.IsNullOrWhiteSpace(p.Title) || p.Title.Trim().Length > 20)
                {
                    return BadRequest(ValidationErrorDto.Create($"Passenger {i + 1}: Title is required and must be max 20 characters.", $"Passengers[{i}].Title"));
                }

                // FirstName is mandatory
                var firstNameVal = TravelValidationHelper.ValidateName(p.FirstName, isRequired: true, $"Passenger {i + 1} first name");
                if (!firstNameVal.IsValid)
                {
                    return BadRequest(ValidationErrorDto.Create(firstNameVal.ErrorMessage!, $"Passengers[{i}].FirstName"));
                }
                p.FirstName = p.FirstName.Trim();

                // LastName is optional: if provided, validate; if omitted, fallback to FirstName for upstream supplier
                if (!string.IsNullOrWhiteSpace(p.LastName))
                {
                    var lastNameVal = TravelValidationHelper.ValidateName(p.LastName, isRequired: false, $"Passenger {i + 1} last name");
                    if (!lastNameVal.IsValid)
                    {
                        return BadRequest(ValidationErrorDto.Create(lastNameVal.ErrorMessage!, $"Passengers[{i}].LastName"));
                    }
                    p.LastName = p.LastName.Trim();
                }
                else
                {
                    p.LastName = p.FirstName; // Mononym fallback for airline GDS/CRS
                }

                // Gender validation
                int gVal = 1;
                var gStr = p.Gender?.Trim() ?? "1";
                if (gStr == "2" || gStr.Equals("Female", StringComparison.OrdinalIgnoreCase)) gVal = 2;
                else if (gStr == "1" || gStr.Equals("Male", StringComparison.OrdinalIgnoreCase)) gVal = 1;
                p.Gender = gVal.ToString();

                var titleGenVal = TravelValidationHelper.ValidateTitleAndGender(p.Title, gVal, $"Passenger {i + 1}");
                if (!titleGenVal.IsValid)
                {
                    return BadRequest(ValidationErrorDto.Create(titleGenVal.ErrorMessage!, $"Passengers[{i}].Title"));
                }

                // Validate PaxType & DOB
                var paxAgeVal = TravelValidationHelper.ValidatePaxTypeAndAge(p.PaxType, p.DateOfBirth, departureDate, $"Passenger {i + 1}");
                if (!paxAgeVal.IsValid)
                {
                    return BadRequest(ValidationErrorDto.Create(paxAgeVal.ErrorMessage!, $"Passengers[{i}].DateOfBirth"));
                }

                // Passport validation if provided
                if (!string.IsNullOrWhiteSpace(p.PassportNo))
                {
                    var passportVal = TravelValidationHelper.ValidatePassport(
                        p.PassportNo, 
                        p.PassportExpiry, 
                        p.PassportIssueDate, 
                        p.PassportIssueCountryCode, 
                        departureDate, 
                        isInternational: false, 
                        $"Passenger {i + 1}");
                    if (!passportVal.IsValid)
                    {
                        return BadRequest(ValidationErrorDto.Create(passportVal.ErrorMessage!, $"Passengers[{i}].PassportNo"));
                    }
                }

                // Secondary passengers inherit contact info if not provided
                if (!p.IsLeadPax)
                {
                    if (string.IsNullOrWhiteSpace(p.Email))
                    {
                        p.Email = leadPax.Email;
                    }
                    else
                    {
                        var emailVal = TravelValidationHelper.ValidateEmail(p.Email, isRequired: false, $"Passenger {i + 1} email");
                        if (!emailVal.IsValid)
                        {
                            return BadRequest(ValidationErrorDto.Create(emailVal.ErrorMessage!, $"Passengers[{i}].Email", "INVALID_EMAIL"));
                        }
                        p.Email = emailVal.CleanedEmail;
                    }

                    if (string.IsNullOrWhiteSpace(p.ContactNo))
                    {
                        p.ContactNo = leadPax.ContactNo;
                    }
                    else
                    {
                        var phoneVal = TravelValidationHelper.ValidateMobileNumber(p.ContactNo, isRequired: false, $"Passenger {i + 1} mobile number");
                        if (!phoneVal.IsValid)
                        {
                            return BadRequest(ValidationErrorDto.Create(phoneVal.ErrorMessage!, $"Passengers[{i}].ContactNo", "INVALID_PHONE"));
                        }
                        p.ContactNo = phoneVal.CleanedPhone;
                    }
                }

                // GST validation if provided
                if (!string.IsNullOrWhiteSpace(p.GSTNumber))
                {
                    var gstVal = TravelValidationHelper.ValidateGstin(p.GSTNumber);
                    if (!gstVal.IsValid)
                    {
                        return BadRequest(ValidationErrorDto.Create($"Passenger {i + 1}: {gstVal.ErrorMessage}", $"Passengers[{i}].GSTNumber"));
                    }
                    if (!string.IsNullOrWhiteSpace(p.GSTCompanyEmail))
                    {
                        var gstEmailVal = TravelValidationHelper.ValidateEmail(p.GSTCompanyEmail, isRequired: true, $"Passenger {i + 1} GST company email");
                        if (!gstEmailVal.IsValid)
                        {
                            return BadRequest(ValidationErrorDto.Create(gstEmailVal.ErrorMessage!, $"Passengers[{i}].GSTCompanyEmail", "INVALID_EMAIL"));
                        }
                        p.GSTCompanyEmail = gstEmailVal.CleanedEmail;
                    }
                }
            }

            var srdvIndex = string.IsNullOrWhiteSpace(proxyRequest.SrdvIndex)
                ? (proxyRequest.ResultIndex?.Contains('_') == true ? proxyRequest.ResultIndex.Split('_')[0] : "1")
                : proxyRequest.SrdvIndex.Trim();
            var srdvType = string.IsNullOrWhiteSpace(proxyRequest.SrdvType) ? "MixAPI" : proxyRequest.SrdvType.Trim();

            var request = new TicketLCCRequestDto
            {
                EndUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                SrdvType = srdvType,
                SrdvIndex = srdvIndex,
                TraceId = proxyRequest.TraceId.ToString(),
                ResultIndex = proxyRequest.ResultIndex,
                RefID = proxyRequest.RefID,
                Module = proxyRequest.Module,
                BookedById = proxyRequest.BookedById,
                BookedByName = proxyRequest.BookedByName,
                CustomerFare = proxyRequest.CustomerFare,
                ReturnCustomerFare = proxyRequest.ReturnCustomerFare,
                CouponCode = proxyRequest.CouponCode,
                PromoCode = proxyRequest.PromoCode,
                PromotionId = proxyRequest.PromotionId,
                JourneyType = proxyRequest.JourneyType,
                Passengers = proxyRequest.Passengers
            };

            try
            {
                var responseRaw = await _srdvFlightService.TicketLCCRawAsync(request);
                var outNode = JsonNode.Parse(responseRaw);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;
                
                bool isSuccess = false;
                bool isPending = false;
                string? errMessage = null;
                int actualErrCode = -1;
                
                if (root.TryGetProperty("ResponseStatus", out var status))
                {
                    if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                    if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                }
                
                if (root.TryGetProperty("Error", out var err))
                {
                    if (err.TryGetProperty("ErrorMessage", out var errMsgProp))
                    {
                        errMessage = errMsgProp.GetString();
                    }

                    if (err.TryGetProperty("ErrorCode", out var errCode))
                    {
                        if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                        if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                        if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;

                        if (errCode.ValueKind == JsonValueKind.Number) actualErrCode = errCode.GetInt32();
                        else if (errCode.ValueKind == JsonValueKind.String && int.TryParse(errCode.ToString(), out var parsedEc)) actualErrCode = parsedEc;

                        // ErrorCode 10 = Pending (booking in process)
                        if (actualErrCode == 10) isPending = true;
                    }
                }

                // Check for expired TraceId or Session in supplier response
                if (!isSuccess && !isPending)
                {
                    var msg = errMessage ?? "";
                    if (msg.Contains("expired", StringComparison.OrdinalIgnoreCase) || 
                        msg.Contains("trace", StringComparison.OrdinalIgnoreCase) ||
                        msg.Contains("session", StringComparison.OrdinalIgnoreCase))
                    {
                        return StatusCode(StatusCodes.Status410Gone, ValidationErrorDto.Create("Flight search session or TraceId has expired. Please refresh flight search to view latest fares and availability.", "TraceId", "TRACE_ID_EXPIRED"));
                    }
                }

                JsonElement resp = root;
                if (root.TryGetProperty("Response", out var responseNode))
                {
                    resp = responseNode;
                }

                // Also detect pending from TicketStatus field
                string ticketStatusStr = "";
                if (resp.TryGetProperty("TicketStatus", out var tsProp))
                {
                    ticketStatusStr = tsProp.ToString() ?? "";
                }

                int ticketStatusCode = -1;
                if (int.TryParse(ticketStatusStr, out var parsedTs))
                {
                    ticketStatusCode = parsedTs;
                }

                bool isPriceChanged = (resp.TryGetProperty("IsPriceChanged", out var ipc) && ipc.ValueKind == JsonValueKind.True) ||
                                      ticketStatusStr == "8" ||
                                      ticketStatusStr.Equals("Price changed", StringComparison.OrdinalIgnoreCase);

                if (ticketStatusStr.Equals("Pending", StringComparison.OrdinalIgnoreCase) || actualErrCode == 10)
                {
                    isPending = true;
                }

                if (isPriceChanged)
                {
                    isSuccess = false;
                }

                // ReturnBookingStatus and ReturnBookingMessage checks (for round-trip / return flights)
                string? returnBookingStatus = resp.TryGetProperty("ReturnBookingStatus", out var rbsProp) ? rbsProp.GetString() : null;
                string? returnBookingMessage = resp.TryGetProperty("ReturnBookingMessage", out var rbmProp) ? rbmProp.GetString() : null;

                bool returnFailed = string.Equals(returnBookingStatus, "Failed", StringComparison.OrdinalIgnoreCase);
                bool returnPending = string.Equals(returnBookingStatus, "Pending", StringComparison.OrdinalIgnoreCase);

                if (returnPending)
                {
                    isPending = true;
                }

                bool ssrDenied = resp.TryGetProperty("SSRDenied", out var ssrDenNode) && ssrDenNode.ValueKind == JsonValueKind.True;
                string? ssrMessage = resp.TryGetProperty("SSRMessage", out var ssrMsgNode) && ssrMsgNode.ValueKind == JsonValueKind.String ? ssrMsgNode.GetString() : null;

                string pnr = resp.TryGetProperty("PNR", out var pnrProp) ? (pnrProp.ToString() ?? "") : "";
                string bookingId = resp.TryGetProperty("BookingId", out var bIdProp) ? (bIdProp.ToString() ?? "") : "";

                if ((isSuccess || isPending) && (!string.IsNullOrEmpty(pnr) || !string.IsNullOrEmpty(bookingId)) && !isPriceChanged)
                {
                    decimal totalFare = 0, baseFare = 0, tax = 0, netFare = 0, customerFare = 0, ssrFromResponse = 0m;
                    string airline = "", airlineCode = "", flightNumber = "", fromCity = "", toCity = "";
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
                                airlineCode = alNode.TryGetProperty("AirlineCode", out var alCodeNode) ? (alCodeNode.GetString() ?? "") : "";
                                var rawName = alNode.TryGetProperty("AirlineName", out var alNameNode) ? (alNameNode.GetString() ?? "") : "";
                                airline = _airlineLookup.GetAirlineName(airlineCode, rawName);
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
                    if (string.IsNullOrWhiteSpace(userIdStr) || userIdStr == "partner_api")
                    {
                        userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                    }
                    
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
                        airlineCode: !string.IsNullOrEmpty(airlineCode) ? airlineCode : airline,
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

                    string reservationStatus = isPending ? "Pending" : (returnFailed ? "PartialSuccess_ReturnFailed" : "Booked");

                    var reservation = new FlightReservation
                    {
                        BookingReference = $"FL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                        Pnr = pnr,
                        UserId = userIdStr,
                        Status = reservationStatus,
                        BookedAtUtc = DateTime.UtcNow,
                        SSRDenied = ssrDenied,
                        SSRMessage = !string.IsNullOrEmpty(returnBookingMessage)
                            ? $"Return Status: {returnBookingStatus}. {returnBookingMessage}. {ssrMessage ?? ""}".Trim()
                            : ssrMessage,
                        
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

                    // If agent or user, deduct wallet
                    if (int.TryParse(userIdStr, out var callerId) && callerId > 0)
                    {
                        var user = await _dbContext.Users.FindAsync(callerId);
                        bool isTicketed = isSuccess && !isPending && !isPriceChanged && (ticketStatusCode == 1 || ticketStatusStr == "1" || ticketStatusStr.Equals("Ticketed", StringComparison.OrdinalIgnoreCase));

                        if (isTicketed && !returnFailed)
                        {
                            if (user != null && user.Role == AuthRoles.Agent)
                            {
                                await _walletService.DebitWalletForBookingAsync(callerId, totalFare, reservation.BookingReference, "Flight", $"Flight Booking LCC PNR {pnr}");
                            }
                            else if (user != null && user.Role == AuthRoles.User)
                            {
                                await _userWalletService.DebitAsync(callerId, reservation.TotalPriceInr, "FlightBooking", reservation.BookingReference, $"Flight Booking LCC PNR {pnr}");
                            }
                        }
                        else if (isTicketed && returnFailed)
                        {
                            _logger.LogWarning("Outbound flight ticketed with PNR {Pnr}, but return leg failed ({Message}). Skipping automatic full wallet debit.", pnr, returnBookingMessage);
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
                _logger.LogError(ex, "Error getting TicketLCC. Attempting recovery via BookingDetails for TraceId {TraceId}.", proxyRequest.TraceId);

                if (proxyRequest.TraceId > 0)
                {
                    try
                    {
                        var recDetails = await _srdvFlightService.GetBookingDetailsRawAsync(proxyRequest.TraceId);
                        if (!string.IsNullOrWhiteSpace(recDetails))
                        {
                            using var rDoc = JsonDocument.Parse(recDetails);
                            var rRoot = rDoc.RootElement;
                            var bStatus = rRoot.TryGetProperty("BookingStatus", out var bsProp) ? bsProp.GetString() : null;
                            if (!string.IsNullOrEmpty(bStatus))
                            {
                                return Ok(JsonNode.Parse(recDetails));
                            }
                        }
                    }
                    catch (Exception recEx)
                    {
                        _logger.LogWarning(recEx, "Recovery query to BookingDetails failed for TraceId {TraceId}", proxyRequest.TraceId);
                    }
                }

                return StatusCode(500, new { message = "Failed to get TicketLCC.", error = ex.Message });
            }
        }

        [HttpPost("BookingDetails")]
        [HttpPost("/v8/BookingDetails")]
        [HttpPost("/api/flight/v8/BookingDetails")]
        [HttpPost("/api/flight/srdv/BookingDetails")]
        public async Task<IActionResult> BookingDetails([FromBody] FlightBookingDetailsProxyRequestDto proxyRequest)
        {
            if (proxyRequest == null || proxyRequest.TraceId <= 0)
            {
                return BadRequest(new { message = "A valid positive TraceId is required." });
            }

            try
            {
                var request = new AirBookingDetailsRequestDto
                {
                    TraceId = proxyRequest.TraceId
                };

                var responseRaw = await _srdvFlightService.GetBookingDetailsRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP exception during Flight BookingDetails for TraceId {TraceId}", proxyRequest?.TraceId);
                return StatusCode(502, new { message = "Supplier communication error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Flight BookingDetails for TraceId {TraceId}", proxyRequest?.TraceId);
                return StatusCode(500, new { message = "Failed to get booking details.", error = ex.Message });
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

            if (request.Passengers != null && request.Passengers.Any())
            {
                var leadPax = request.Passengers.FirstOrDefault(p => p.IsLeadPax) ?? request.Passengers.First();
                if (!string.IsNullOrWhiteSpace(leadPax.Email))
                {
                    var leadEmailVal = TravelValidationHelper.ValidateEmail(leadPax.Email, isRequired: true, "Lead passenger email");
                    if (!leadEmailVal.IsValid)
                    {
                        return BadRequest(ValidationErrorDto.Create(leadEmailVal.ErrorMessage!, "Email", "INVALID_EMAIL"));
                    }
                    leadPax.Email = leadEmailVal.CleanedEmail;
                }

                for (int i = 0; i < request.Passengers.Count; i++)
                {
                    var p = request.Passengers[i];
                    if (!string.IsNullOrWhiteSpace(p.Email))
                    {
                        var emailVal = TravelValidationHelper.ValidateEmail(p.Email, isRequired: false, $"Passenger {i + 1} email");
                        if (!emailVal.IsValid)
                        {
                            return BadRequest(ValidationErrorDto.Create(emailVal.ErrorMessage!, $"Passengers[{i}].Email", "INVALID_EMAIL"));
                        }
                        p.Email = emailVal.CleanedEmail;
                    }
                }
            }

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
                    string airline = "", airlineCode = "", flightNumber = "", fromCity = "", toCity = "";
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
                            if (firstSeg.TryGetProperty("Airline", out var alNode))
                            {
                                airlineCode = alNode.TryGetProperty("AirlineCode", out var alCodeNode) ? (alCodeNode.GetString() ?? "") : "";
                                var rawName = alNode.TryGetProperty("AirlineName", out var alNameNode) ? (alNameNode.GetString() ?? "") : "";
                                airline = _airlineLookup.GetAirlineName(airlineCode, rawName);
                            }
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
                        airlineCode: !string.IsNullOrEmpty(airlineCode) ? airlineCode : airline,
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
                        if (int.TryParse(reservation.UserId, out var callerId) && callerId > 0)
                        {
                            var user = await _dbContext.Users.FindAsync(callerId);
                            if (user != null && user.Role == AuthRoles.Agent)
                            {
                                if (isSuccess && !isPending && !isPriceChanged && ticketStatusCode == 1)
                                {
                                    await _walletService.DebitWalletForBookingAsync(callerId, reservation.SupplierTotalFare, reservation.BookingReference, "Flight", $"Flight Booking GDS PNR {pnr}");
                                }
                            }
                            else if (user != null && user.Role == AuthRoles.User)
                            {
                                if (isSuccess && !isPending && !isPriceChanged && ticketStatusCode == 1)
                                {
                                    await _userWalletService.DebitAsync(callerId, reservation.TotalPriceInr, "FlightBooking", reservation.BookingReference, $"Flight Booking GDS PNR {pnr}");
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
                    string airline = "", airlineCode = "", flightNumber = "", fromCity = "", toCity = "";
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
                            if (firstSeg.TryGetProperty("Airline", out var alNode))
                            {
                                airlineCode = alNode.TryGetProperty("AirlineCode", out var alCodeNode) ? (alCodeNode.GetString() ?? "") : "";
                                var rawName = alNode.TryGetProperty("AirlineName", out var alNameNode) ? (alNameNode.GetString() ?? "") : "";
                                airline = _airlineLookup.GetAirlineName(airlineCode, rawName);
                            }
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
                        airlineCode: !string.IsNullOrEmpty(airlineCode) ? airlineCode : airline,
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


        [AllowAnonymous]
        [InjectClientIp]
        [HttpPost("SendChangeRequest")]
        [HttpPost("/v8/SendChangeRequest")]
        [HttpPost("/api/flight/v8/SendChangeRequest")]
        [HttpPost("/api/flight/srdv/SendChangeRequest")]
        public async Task<IActionResult> SendChangeRequest([FromBody] FlightSendChangeProxyRequestDto proxyRequest)
        {
            string? currentUserId = null;
            bool isPartnerApi = false;
            if (Request.Headers.TryGetValue("Api-Token", out var apiTok) && !string.IsNullOrWhiteSpace(apiTok))
            {
                isPartnerApi = true;
                currentUserId = "partner_api";
            }
            else if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("id")?.Value;
            }
            else
            {
                return Unauthorized(new { ErrorCode = 1, ErrorMessage = "Please login or provide a valid Api-Token header." });
            }

            if (proxyRequest == null)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "Request body cannot be empty." });
            }

            if (proxyRequest.BookingId <= 0)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "A valid positive BookingId is required." });
            }

            if (proxyRequest.RequestType < 0 || proxyRequest.RequestType > 3)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "RequestType must be 0, 1, 2, or 3." });
            }

            if (proxyRequest.CancellationType < 0 || proxyRequest.CancellationType > 3)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "CancellationType must be 0, 1, 2, or 3." });
            }

            if (string.IsNullOrWhiteSpace(proxyRequest.PNR) || proxyRequest.PNR.Trim().Length > 50)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "PNR is required and must be between 1 and 50 characters." });
            }

            if (string.IsNullOrWhiteSpace(proxyRequest.Remarks) || proxyRequest.Remarks.Trim().Length > 2000)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "Remarks is required and must be between 1 and 2000 characters." });
            }

            if (!string.IsNullOrWhiteSpace(proxyRequest.ClientRefId) && proxyRequest.ClientRefId.Trim().Length > 200)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "ClientRefId cannot exceed 200 characters." });
            }

            if (proxyRequest.Sectors == null || !proxyRequest.Sectors.Any() || proxyRequest.Sectors.Count > 6)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "Between 1 and 6 sectors are required." });
            }

            foreach (var s in proxyRequest.Sectors)
            {
                if (string.IsNullOrWhiteSpace(s.Origin) || s.Origin.Trim().Length != 3 ||
                    string.IsNullOrWhiteSpace(s.Destination) || s.Destination.Trim().Length != 3)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "Sector Origin and Destination must be 3-character IATA airport codes." });
                }
            }

            if (proxyRequest.TicketData == null || !proxyRequest.TicketData.Any() || proxyRequest.TicketData.Count > 9)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "Between 1 and 9 passenger tickets are required in TicketData." });
            }

            foreach (var t in proxyRequest.TicketData)
            {
                if (string.IsNullOrWhiteSpace(t.FirstName) || t.FirstName.Trim().Length > 100 ||
                    string.IsNullOrWhiteSpace(t.LastName) || t.LastName.Trim().Length > 100)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "Passenger FirstName and LastName are required (1-100 characters)." });
                }
                if (!string.IsNullOrEmpty(t.TicketId) && t.TicketId.Trim().Length > 100)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "TicketId cannot exceed 100 characters." });
                }
            }

            try
            {
                bool isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

                var bookingIdStr = proxyRequest.BookingId.ToString();
                var reservation = await _dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(r => r.SrdvBookingId == bookingIdStr);
                if (reservation == null && !string.IsNullOrEmpty(proxyRequest.PNR))
                {
                    reservation = await _dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(r => r.Pnr == proxyRequest.PNR.Trim());
                }

                if (reservation == null)
                {
                    return NotFound(new { ErrorCode = 1, ErrorMessage = "Flight reservation not found for the provided BookingId or PNR." });
                }

                // Strict Tenant Isolation / IDOR Protection (for portal users/agents)
                if (!isPartnerApi && !isAdmin && !string.Equals(reservation.UserId, currentUserId, StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { ErrorCode = 1, ErrorMessage = "Unauthorized: You do not have permission to modify or cancel this booking." });
                }

                // Status Guards
                if (string.Equals(reservation.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "This flight booking is already cancelled." });
                }

                if (string.Equals(reservation.Status, "Cancellation Requested", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(reservation.Status, "Partial Cancellation Requested", StringComparison.OrdinalIgnoreCase))
                {
                    var hasActiveCancel = await _dbContext.FlightCancellationRequests
                        .AnyAsync(c => c.FlightReservationId == reservation.Id && (c.CancellationStatus == "Pending" || c.CancellationStatus == "IN_PROCESS"));
                    if (hasActiveCancel)
                    {
                        return BadRequest(new { ErrorCode = 1, ErrorMessage = "A cancellation request is already pending for this booking." });
                    }
                }

                var request = new SendChangeRequestDto
                {
                    BookingId = proxyRequest.BookingId,
                    RequestType = proxyRequest.RequestType,
                    CancellationType = proxyRequest.CancellationType,
                    Remarks = proxyRequest.Remarks.Trim(),
                    ClientRefId = string.IsNullOrWhiteSpace(proxyRequest.ClientRefId) ? string.Empty : proxyRequest.ClientRefId.Trim(),
                    Sectors = proxyRequest.Sectors,
                    TicketData = proxyRequest.TicketData,
                    PNR = proxyRequest.PNR.Trim()
                };

                var responseRaw = await _srdvFlightService.SendChangeRequestRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;
                
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
                        bool anyAccepted = false;
                        string changeRequestId = "";
                        if (resp.TryGetProperty("TicketCRInfo", out var crInfo) && crInfo.ValueKind == JsonValueKind.Array && crInfo.GetArrayLength() > 0)
                        {
                            foreach (var row in crInfo.EnumerateArray())
                            {
                                var rowStatus = row.TryGetProperty("Status", out var stProp) && stProp.ValueKind == JsonValueKind.Number ? stProp.GetInt32() : 0;
                                if (rowStatus == 1) // Pending / Accepted
                                {
                                    anyAccepted = true;
                                    if (string.IsNullOrEmpty(changeRequestId) && row.TryGetProperty("ChangeRequestId", out var crIdNode))
                                        changeRequestId = crIdNode.ToString();
                                }
                            }
                        }

                        if (anyAccepted)
                        {
                            var isPartial = false;
                            var reqSectors = request.Sectors != null && request.Sectors.Any() ? System.Text.Json.JsonSerializer.Serialize(request.Sectors) : null;
                            var reqTickets = request.TicketData != null && request.TicketData.Any() ? System.Text.Json.JsonSerializer.Serialize(request.TicketData) : null;

                            if (request.Sectors != null && request.Sectors.Any() && request.Sectors.Count < reservation.Segments.Count)
                                isPartial = true;
                            if (request.TicketData != null && request.TicketData.Any() && request.TicketData.Count < reservation.SeatsBooked)
                                isPartial = true;

                            var cancelReq = new FlightCancellationRequest
                            {
                                FlightReservationId = reservation.Id,
                                RequestDateUtc = DateTime.UtcNow,
                                CancellationStatus = "Pending",
                                CustomerRefundStatus = "Pending",
                                AdminRefundStatus = "Pending",
                                SrdvChangeRequestId = changeRequestId,
                                SrdvBookingId = bookingIdStr,
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

                            // Additive In-App Notifications
                            try
                            {
                                var inAppNotificationService = HttpContext.RequestServices.GetService<PickNBook.Api.Services.Interfaces.IInAppNotificationService>();
                                if (inAppNotificationService != null)
                                {
                                    await inAppNotificationService.CreateNotificationAsync(
                                        type: "Cancellation",
                                        category: "Customer",
                                        title: "Flight Cancellation Requested",
                                        message: $"Your cancellation request for flight booking ({reservation.BookingReference}) has been submitted and is processing.",
                                        severity: "Info",
                                        referenceType: "FlightReservation",
                                        referenceId: reservation.BookingReference,
                                        actionUrl: $"/bookings/{reservation.BookingReference}",
                                        idempotencyKey: $"CANCEL_REQ_FLIGHT_{reservation.BookingReference}_{changeRequestId}",
                                        targetUserId: reservation.UserId
                                    );
                                }
                            }
                            catch (Exception inAppEx)
                            {
                                _logger.LogWarning(inAppEx, "Failed to create in-app notification for flight cancellation request {BookingReference}. Non-fatal.", reservation.BookingReference);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("SendChangeRequest returned 0 ErrorCode but all tickets were refused (Status 4) for BookingId {BookingId}", proxyRequest.BookingId);
                        }
                    }
                }

                return Ok(doc.RootElement.Clone());
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP exception during SendChangeRequest for BookingId {BookingId}, PNR {PNR}", proxyRequest?.BookingId, proxyRequest?.PNR);
                return StatusCode(502, new { ErrorCode = 502, ErrorMessage = "Supplier communication error.", Details = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending change request for BookingId {BookingId}, PNR {PNR}", proxyRequest?.BookingId, proxyRequest?.PNR);
                return StatusCode(500, new { ErrorCode = 500, ErrorMessage = "Failed to send change request.", Details = ex.Message });
            }
        }

        [AllowAnonymous]
        [InjectClientIp]
        [HttpPost("GetCancelStatus")]
        [HttpPost("/v8/GetCancelStatus")]
        [HttpPost("/api/flight/v8/GetCancelStatus")]
        [HttpPost("/api/flight/srdv/GetCancelStatus")]
        public async Task<IActionResult> GetCancelStatus([FromBody] FlightGetCancelStatusProxyRequestDto proxyRequest)
        {
            if (!Request.Headers.TryGetValue("Api-Token", out var apiTok) || string.IsNullOrWhiteSpace(apiTok))
            {
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    return Unauthorized(new { ErrorCode = 1, ErrorMessage = "Please login or provide a valid Api-Token header." });
                }
            }

            if (proxyRequest == null || proxyRequest.ChangeRequestId <= 0)
            {
                return BadRequest(new { ErrorCode = 1, ErrorMessage = "A valid positive ChangeRequestId is required." });
            }

            try
            {
                var request = new GetCancelStatusRequestDto
                {
                    ChangeRequestId = proxyRequest.ChangeRequestId
                };
                var responseRaw = await _srdvFlightService.GetCancelStatusRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                var root = doc.RootElement;

                // Synchronize real-time DB state
                var crIdStr = proxyRequest.ChangeRequestId.ToString();
                var cancelReq = await _dbContext.FlightCancellationRequests
                    .Include(c => c.FlightReservation)
                    .FirstOrDefaultAsync(c => c.SrdvChangeRequestId == crIdStr);

                if (cancelReq != null)
                {
                    var cStatus = root.TryGetProperty("CancellationStatus", out var csProp) ? csProp.GetString() : null;
                    var rStatus = root.TryGetProperty("RefundStatus", out var rsProp) ? rsProp.GetString() : null;
                    var isSettled = root.TryGetProperty("IsSettled", out var isProp) && isProp.ValueKind == JsonValueKind.True;
                    decimal? refundAmt = root.TryGetProperty("RefundAmount", out var raProp) && raProp.ValueKind == JsonValueKind.Number ? raProp.GetDecimal() : null;
                    decimal? cancelCharge = root.TryGetProperty("CancellationCharge", out var ccProp) && ccProp.ValueKind == JsonValueKind.Number ? ccProp.GetDecimal() : null;

                    if (!string.IsNullOrEmpty(cStatus)) cancelReq.CancellationStatus = cStatus;
                    if (refundAmt.HasValue) cancelReq.AdminRefundAmountInr = refundAmt.Value;
                    if (cancelCharge.HasValue) cancelReq.AdminCancellationChargeInr = cancelCharge.Value;

                    if (cancelReq.FlightReservation != null)
                    {
                        if (cStatus == "CANCELLED")
                        {
                            cancelReq.FlightReservation.Status = cancelReq.IsPartialCancellation ? "Partially Cancelled" : "Cancelled";
                            cancelReq.FlightReservation.CancelledAtUtc ??= DateTime.UtcNow;
                        }
                        else if (cStatus == "FAILED")
                        {
                            cancelReq.FlightReservation.Status = "Booked";
                        }
                    }

                    var bookingCanc = await _dbContext.BookingCancellations.FirstOrDefaultAsync(b => b.SrdvChangeRequestId == crIdStr);
                    if (bookingCanc != null)
                    {
                        if (!string.IsNullOrEmpty(cStatus)) bookingCanc.SrdvStatus = cStatus;
                        if (refundAmt.HasValue) bookingCanc.SupplierRefundAmount = refundAmt.Value;
                        if (cancelCharge.HasValue) bookingCanc.SupplierCancellationCharge = cancelCharge.Value;
                        if (isSettled) bookingCanc.Status = "Settled";
                    }

                    await _dbContext.SaveChangesAsync();
                }

                return Ok(doc.RootElement.Clone());
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP exception during GetCancelStatus for ChangeRequestId {ChangeRequestId}", proxyRequest?.ChangeRequestId);
                return StatusCode(502, new { ErrorCode = 502, ErrorMessage = "Supplier communication error.", Details = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cancel status for ChangeRequestId {ChangeRequestId}", proxyRequest?.ChangeRequestId);
                return StatusCode(500, new { ErrorCode = 500, ErrorMessage = "Failed to get cancel status.", Details = ex.Message });
            }
        }

        [AllowAnonymous]
        [InjectClientIp]
        [HttpPost("GetCancellationCharges")]
        [HttpPost("/v8/GetCancellationCharges")]
        [HttpPost("/api/flight/v8/GetCancellationCharges")]
        [HttpPost("/api/flight/srdv/GetCancellationCharges")]
        public async Task<IActionResult> GetCancellationCharges([FromBody] FlightGetCancellationChargesProxyRequestDto proxyRequest)
        {
            if (!Request.Headers.TryGetValue("Api-Token", out var apiTok) || string.IsNullOrWhiteSpace(apiTok))
            {
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    return Unauthorized(new { ErrorCode = 1, ErrorMessage = "Please login or provide a valid Api-Token header." });
                }
            }

            try
            {
                if (proxyRequest == null || proxyRequest.TraceId <= 0)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "TraceId is required and must be greater than 0." });
                }

                if (!string.IsNullOrEmpty(proxyRequest.PNR) && proxyRequest.PNR.Length > 50)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "PNR cannot exceed 50 characters." });
                }

                if (!string.IsNullOrEmpty(proxyRequest.Remarks) && proxyRequest.Remarks.Length > 500)
                {
                    return BadRequest(new { ErrorCode = 1, ErrorMessage = "Remarks cannot exceed 500 characters." });
                }

                var request = new GetCancellationChargesRequestDto
                {
                    TraceId = proxyRequest.TraceId,
                    PNR = proxyRequest.PNR?.Trim() ?? string.Empty,
                    Remarks = proxyRequest.Remarks?.Trim() ?? string.Empty
                };

                var responseRaw = await _srdvFlightService.GetCancellationChargesRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cancellation charges for TraceId: {TraceId}", proxyRequest?.TraceId);
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
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public async Task<IActionResult> BookingCallback([FromBody] FlightBookingCallbackProxyRequestDto proxyRequest)
        {
            try
            {
                _logger.LogInformation("Received SRDV Booking Update Callback for BookingId: {BookingId}, PNR: {PNR}", proxyRequest.BookingId, proxyRequest.PNR);

                // 1. Security Check: Authenticate on the X-SRDV-Token header carrying Api-Token (with fallback to Api-Token)
                var tokenHeader = Request.Headers["X-SRDV-Token"].FirstOrDefault()
                                  ?? Request.Headers["Api-Token"].FirstOrDefault();

                bool isTokenValid = !string.IsNullOrWhiteSpace(tokenHeader) && string.Equals(tokenHeader, _srdvSettings.ApiToken, StringComparison.Ordinal);

                // Per SRDV v8 Spec: "The header is left out when the token on file is no longer valid — treat such a call as unverified and read BookingDetails instead."
                if (!isTokenValid)
                {
                    _logger.LogWarning("SRDV Booking Callback unverified (missing or mismatched X-SRDV-Token) for BookingId: {BookingId}, TraceId: {TraceId}. Attempting verification via BookingDetails.", proxyRequest.BookingId, proxyRequest.TraceId);

                    if (proxyRequest.TraceId > 0)
                    {
                        try
                        {
                            var verifiedDetails = await _srdvFlightService.GetBookingDetailsRawAsync(proxyRequest.TraceId);
                            if (!string.IsNullOrWhiteSpace(verifiedDetails))
                            {
                                using var vDoc = JsonDocument.Parse(verifiedDetails);
                                var vRoot = vDoc.RootElement;
                                var vStatus = vRoot.TryGetProperty("BookingStatus", out var bsProp) ? bsProp.GetString() : null;
                                if (!string.IsNullOrEmpty(vStatus))
                                {
                                    _logger.LogInformation("Successfully verified booking state via BookingDetails for TraceId {TraceId}: Status {Status}", proxyRequest.TraceId, vStatus);
                                    isTokenValid = true;
                                }
                            }
                        }
                        catch (Exception recEx)
                        {
                            _logger.LogWarning(recEx, "BookingDetails verification query failed for TraceId {TraceId}", proxyRequest.TraceId);
                        }
                    }

                    if (!isTokenValid)
                    {
                        _logger.LogWarning("SRDV Booking Callback failed verification for BookingId: {BookingId}.", proxyRequest.BookingId);
                        return Unauthorized(new { message = "Unauthorized: Invalid or missing X-SRDV-Token header." });
                    }
                }

                // 2. Event verification: branch on "BOOKING_STATUS"
                if (!string.IsNullOrWhiteSpace(proxyRequest.Event) && !string.Equals(proxyRequest.Event, "BOOKING_STATUS", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("SRDV Booking Callback ignored non-booking event: {Event}", proxyRequest.Event);
                    return Ok("Successfully Updated.");
                }

                var bookingIdStr = proxyRequest.BookingId > 0 ? proxyRequest.BookingId.ToString() : string.Empty;
                var pnr = proxyRequest.PNR?.Trim() ?? string.Empty;
                var traceIdStr = proxyRequest.TraceId > 0 ? proxyRequest.TraceId.ToString() : string.Empty;

                if (string.IsNullOrEmpty(bookingIdStr) && string.IsNullOrEmpty(pnr) && string.IsNullOrEmpty(traceIdStr))
                {
                    _logger.LogWarning("Received callback with missing PNR, TraceId, and BookingId.");
                    return Ok("Successfully Updated.");
                }

                var reservationQuery = _dbContext.FlightReservations.AsQueryable();

                if (!string.IsNullOrEmpty(bookingIdStr))
                {
                    reservationQuery = reservationQuery.Where(r => r.SrdvBookingId == bookingIdStr);
                }
                else if (!string.IsNullOrEmpty(pnr))
                {
                    reservationQuery = reservationQuery.Where(r => r.Pnr == pnr);
                }
                else if (!string.IsNullOrEmpty(traceIdStr))
                {
                    reservationQuery = reservationQuery.Where(r => r.TraceId == traceIdStr);
                }

                var reservation = await reservationQuery.FirstOrDefaultAsync();

                if (reservation != null)
                {
                    reservation.SrdvCallbackResponseJson = JsonSerializer.Serialize(proxyRequest);
                    reservation.CallbackReceivedAtUtc = DateTime.UtcNow;

                    if (proxyRequest.SrdvIndex > 0)
                    {
                        reservation.SrdvIndex = proxyRequest.SrdvIndex.ToString();
                    }

                    if (!string.IsNullOrEmpty(pnr) && string.IsNullOrEmpty(reservation.Pnr))
                    {
                        reservation.Pnr = pnr;
                    }

                    if (!string.IsNullOrEmpty(proxyRequest.GdsPNR) && string.IsNullOrEmpty(reservation.GdsPnr))
                    {
                        reservation.GdsPnr = proxyRequest.GdsPNR.Trim();
                    }

                    if (!string.IsNullOrEmpty(proxyRequest.ReturnPNR))
                    {
                        reservation.ReturnPnr = proxyRequest.ReturnPNR.Trim();
                    }

                    var rawStatus = proxyRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
                    reservation.TicketStatus = rawStatus;

                    switch (rawStatus)
                    {
                        case "SUCCESS":
                        case "TICKETED":
                            var wasAlreadyBooked = string.Equals(reservation.Status, "Booked", StringComparison.OrdinalIgnoreCase);
                            reservation.Status = "Booked";

                            // If reservation was pending and wallet was not debited yet, settle wallet now
                            if (!wasAlreadyBooked && reservation.WalletPaidAmount == 0 && int.TryParse(reservation.UserId, out var callerId) && callerId > 0)
                            {
                                var user = await _dbContext.Users.FindAsync(callerId);
                                if (user != null && user.Role == AuthRoles.Agent && reservation.SupplierTotalFare > 0)
                                {
                                    await _walletService.DebitWalletForBookingAsync(callerId, reservation.SupplierTotalFare, reservation.BookingReference, "Flight", $"Flight Booking LCC PNR {pnr}");
                                    reservation.WalletPaidAmount = reservation.SupplierTotalFare;
                                }
                                else if (user != null && user.Role == AuthRoles.User && reservation.TotalPriceInr > 0)
                                {
                                    await _userWalletService.DebitAsync(callerId, reservation.TotalPriceInr, "FlightBooking", reservation.BookingReference, $"Flight Booking LCC PNR {pnr}");
                                    reservation.WalletPaidAmount = reservation.TotalPriceInr;
                                }
                            }

                            // Dispatch final email only once if newly confirmed
                            if (!wasAlreadyBooked)
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
                            break;

                        case "FAILED":
                        case "ABORTED":
                            // Idempotency: only credit wallet if it was actually charged
                            var wasAlreadyFailed = string.Equals(reservation.Status, "Failed", StringComparison.OrdinalIgnoreCase);
                            reservation.Status = "Failed";

                            if (!wasAlreadyFailed && reservation.WalletPaidAmount > 0)
                            {
                                if (int.TryParse(reservation.UserId, out var agentId) && agentId > 0)
                                {
                                    var user = await _dbContext.Users.FindAsync(agentId);
                                    if (user != null && user.Role == AuthRoles.Agent)
                                    {
                                        await _walletService.CreditWalletForRefundAsync(agentId,
                                            reservation.WalletPaidAmount,
                                            reservation.BookingReference,
                                            "Flight",
                                            $"Refund - Failed Flight Booking PNR {reservation.Pnr}");
                                        reservation.WalletPaidAmount = 0m;
                                    }
                                }
                            }
                            break;

                        case "MANUAL_CHECK_REQUIRED":
                            // Per SRDV documentation: MANUAL_CHECK_REQUIRED is not a failure.
                            // The supplier never answered clearly, the money stays reserved, and SRDV settles it by hand.
                            // Hold the booking rather than releasing or refunding it against that status.
                            reservation.Status = "ManualCheckRequired";
                            _logger.LogWarning("Flight reservation {BookingReference} entered MANUAL_CHECK_REQUIRED. Holding booking without refund.", reservation.BookingReference);
                            break;

                        case "CANCELLED":
                            reservation.Status = "Cancelled";
                            reservation.CancelledAtUtc ??= DateTime.UtcNow;
                            if (!string.IsNullOrEmpty(proxyRequest.Remark))
                            {
                                reservation.CancellationReason = proxyRequest.Remark;
                            }
                            break;

                        case "PENDING":
                            if (reservation.Status != "Booked")
                            {
                                reservation.Status = "Pending";
                            }
                            break;
                    }

                    // Handle CancellationStatus if sent in callback
                    if (!string.IsNullOrEmpty(proxyRequest.CancellationStatus) && !string.Equals(proxyRequest.CancellationStatus, "NOT_CANCELLED", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(proxyRequest.CancellationStatus, "FULLY_CANCELLED", StringComparison.OrdinalIgnoreCase))
                        {
                            reservation.Status = "Cancelled";
                            reservation.CancelledAtUtc ??= DateTime.UtcNow;
                        }
                        else if (string.Equals(proxyRequest.CancellationStatus, "PARTIALLY_CANCELLED", StringComparison.OrdinalIgnoreCase))
                        {
                            reservation.Status = "Partially Cancelled";
                        }
                    }

                    // Sync Passenger Ticket Numbers & Details in booking order
                    if (proxyRequest.Passengers != null && proxyRequest.Passengers.Any())
                    {
                        var reservationPassengers = await _dbContext.FlightReservationPassengers
                            .Where(p => p.FlightReservationId == reservation.Id)
                            .ToListAsync();

                        for (int i = 0; i < proxyRequest.Passengers.Count; i++)
                        {
                            var incPax = proxyRequest.Passengers[i];
                            var dbPax = (i < reservationPassengers.Count)
                                ? reservationPassengers[i]
                                : reservationPassengers.FirstOrDefault(p =>
                                    string.Equals(p.FirstName, incPax.FirstName, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(p.LastName, incPax.LastName, StringComparison.OrdinalIgnoreCase));

                            if (dbPax != null)
                            {
                                if (!string.IsNullOrEmpty(incPax.TicketNumber))
                                {
                                    dbPax.TicketNumber = incPax.TicketNumber;
                                }
                                if (string.IsNullOrEmpty(dbPax.Title) && !string.IsNullOrEmpty(incPax.Title))
                                {
                                    dbPax.Title = incPax.Title;
                                }
                            }
                        }
                    }

                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Successfully updated reservation {ReservationId} with callback data. Final Status: {Status}", reservation.Id, reservation.Status);
                }
                else
                {
                    _logger.LogWarning("Received callback for PNR {PNR} / BookingId {BookingId} but no matching reservation was found.", pnr, bookingIdStr);
                }

                // Exactly match the required success response structure: Answer 2xx ("Successfully Updated.")
                return Ok("Successfully Updated.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SRDV Booking Callback.");
                return StatusCode(500, new { message = "Internal Server Error" });
            }
        }

        [Authorize]
        [HttpGet("my-bookings")]
        [HttpGet("/api/flight/my-bookings")]
        [HttpGet("/api/flight/bookings")]
        [HttpGet("/api/flight/srdv/bookings")]
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

                var flightIds = bookings.Select(b => b.Id).ToList();
                var bookingRefs = bookings.Select(b => b.BookingReference).Where(r => !string.IsNullOrEmpty(r)).ToList();

                var payments = await _dbContext.Payments.AsNoTracking()
                    .Where(p => (p.BookingType == "Flight" || p.BookingType.ToLower() == "flight") &&
                                ((p.BookingId.HasValue && flightIds.Contains(p.BookingId.Value)) ||
                                 (p.BookingReferenceId.HasValue && flightIds.Contains(p.BookingReferenceId.Value)) ||
                                 (p.PaymentReference != null && bookingRefs.Contains(p.PaymentReference))))
                    .ToListAsync();

                var paymentMapById = payments
                    .Where(p => p.BookingId.HasValue)
                    .GroupBy(p => p.BookingId!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

                var paymentMapByRefId = payments
                    .Where(p => p.BookingReferenceId.HasValue)
                    .GroupBy(p => p.BookingReferenceId!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

                var paymentMapByPaymentRef = payments
                    .Where(p => !string.IsNullOrEmpty(p.PaymentReference))
                    .GroupBy(p => p.PaymentReference)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

                var paymentIds = payments.Select(p => p.Id).ToList();
                var cancellations = await _dbContext.BookingCancellations.AsNoTracking()
                    .Where(c => (c.BookingType == "Flight" || c.BookingType.ToLower() == "flight") &&
                                (paymentIds.Contains(c.PaymentId) || bookingRefs.Contains(c.BookingReference)))
                    .ToListAsync();
                var cancelMapByPayment = cancellations
                    .Where(c => c.PaymentId > 0)
                    .GroupBy(c => c.PaymentId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).FirstOrDefault());
                var cancelMapByRef = cancellations
                    .GroupBy(c => c.BookingReference)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).FirstOrDefault());

                var execs = paymentIds.Any()
                    ? await _dbContext.SupplierFulfillmentExecutions.AsNoTracking()
                        .Where(e => paymentIds.Contains(e.PaymentId))
                        .ToListAsync()
                    : new List<PickNBook.Api.Models.Entities.SupplierFulfillmentExecution>();
                var execMap = execs.GroupBy(e => e.PaymentId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).FirstOrDefault());

                var allPassengers = await _dbContext.FlightReservationPassengers.AsNoTracking()
                    .Where(p => flightIds.Contains(p.FlightReservationId))
                    .ToListAsync();
                var paxMap = allPassengers.GroupBy(p => p.FlightReservationId).ToDictionary(g => g.Key, g => g.ToList());

                var result = new List<MyFlightBookingResponseDto>();

                foreach (var booking in bookings)
                {
                    paxMap.TryGetValue(booking.Id, out var passengers);
                    passengers ??= new List<FlightReservationPassenger>();

                    PickNBook.Api.Models.Payments.Payment? payment = null;
                    if (!paymentMapByRefId.TryGetValue(booking.Id, out payment))
                    {
                        if (!paymentMapById.TryGetValue(booking.Id, out payment))
                        {
                            if (!string.IsNullOrEmpty(booking.BookingReference))
                            {
                                paymentMapByPaymentRef.TryGetValue(booking.BookingReference, out payment);
                            }
                        }
                    }

                    PickNBook.Api.Models.Entities.BookingCancellation? cancel = null;
                    if (payment != null) cancelMapByPayment.TryGetValue(payment.Id, out cancel);
                    if (cancel == null && !string.IsNullOrEmpty(booking.BookingReference)) cancelMapByRef.TryGetValue(booking.BookingReference, out cancel);
                    PickNBook.Api.Models.Entities.SupplierFulfillmentExecution? exec = null;
                    if (payment != null) execMap.TryGetValue(payment.Id, out exec);

                    decimal cancelCharges = cancel?.SupplierCancellationCharge ?? booking.CancellationChargeInr ?? 0m;
                    decimal refundAmt = cancel?.CustomerRefundAmount ?? booking.RefundAmountInr ?? (string.Equals(payment?.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) ? (payment?.FinalPayableAmount ?? booking.TotalPriceInr) : 0m);

                    var lifecycle = PickNBook.Api.Helpers.BookingLifecycleHelper.Build(
                        "Flight",
                        booking.Id,
                        booking.BookingReference,
                        booking.Pnr,
                        booking.Status,
                        $"{booking.Airline ?? "Flight"} ({booking.FromCity} → {booking.ToCity})",
                        booking.DepartureTime,
                        payment,
                        exec,
                        cancel,
                        booking.TotalPriceInr,
                        cancelCharges,
                        refundAmt);

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
                        }).ToList(),

                        // Payment & Contact Identifiers
                        PaymentId = payment?.Id,
                        CashfreePaymentId = payment?.CashfreePaymentId,
                        CashfreeOrderId = payment?.CashfreeOrderId,
                        PaymentReference = payment?.PaymentReference,
                        PassengerName = booking.PassengerName,
                        PassengerPhone = booking.PassengerPhone,
                        PhoneNumber = booking.PassengerPhone,
                        Phone = booking.PassengerPhone,
                        PassengerEmail = booking.PassengerEmail,

                        CanonicalStatus = lifecycle.CanonicalStatus,
                        CanonicalStatusLabel = lifecycle.CanonicalStatusLabel,
                        LifecycleHierarchy = lifecycle.Hierarchy,
                        Timeline = lifecycle.Timeline,
                        PaymentBreakdown = lifecycle.PaymentBreakdown,
                        CancellationAudit = lifecycle.CancellationAudit
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




















