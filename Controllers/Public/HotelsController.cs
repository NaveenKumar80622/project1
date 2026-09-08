using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using PickNBook.Api.Filters;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HotelsController : ControllerBase
    {
        private readonly IHotelService _hotelService;
        private readonly AppDbContext _dbContext;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<HotelsController> _logger;
        private readonly ITicketEmailService _ticketEmailService;
        private readonly IMemoryCache _cache;
        private readonly PickNBook.Api.Services.Interfaces.ICancellationRefundCalculator _refundCalculator;
        private readonly PickNBook.Api.Services.Interfaces.ICashfreeService _cashfreeService;

        public HotelsController(
            IHotelService hotelService,
            AppDbContext dbContext,
            ICurrentUserService currentUserService,
            ILogger<HotelsController> logger,
            ITicketEmailService ticketEmailService,
            IMemoryCache cache,
            PickNBook.Api.Services.Interfaces.ICancellationRefundCalculator refundCalculator,
            PickNBook.Api.Services.Interfaces.ICashfreeService cashfreeService)
        {
            _hotelService = hotelService;
            _dbContext = dbContext;
            _currentUserService = currentUserService;
            _logger = logger;
            _ticketEmailService = ticketEmailService;
            _cache = cache;
            _refundCalculator = refundCalculator;
            _cashfreeService = cashfreeService;
        }

        // =====================================
        // SEARCH HOTELS
        // =====================================


        [HttpPost("SearchHotels")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> SearchHotelsMultiLevelPost([FromBody] SrdvHotelSearchRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            bool hasCityId = !string.IsNullOrWhiteSpace(request.CityId);
            bool hasHotelCodes = request.HotelCodes != null && request.HotelCodes.Count > 0;

            if (!hasCityId && !hasHotelCodes)
            {
                return BadRequest(new { message = "Either CityId or HotelCodes must be provided." });
            }

            if (string.IsNullOrWhiteSpace(request.CheckInDate))
            {
                return BadRequest(new { message = "CheckInDate is required (format: YYYY-MM-DD)." });
            }

            try
            {
                var response = await _hotelService.SearchHotelsMultiLevelAsync(request);

                // Fire-and-forget logging to the database
                var userId = _currentUserService.GetUserOrGuestId();
                var cityId = hasCityId ? request.CityId.Trim().ToUpperInvariant() : (request.HotelCodes != null ? string.Join(",", request.HotelCodes) : "HOTEL_CODES");
                var checkInStr = request.CheckInDate;
                var checkOutStr = request.CheckOutDate;
                var roomGuests = request.RoomGuests;
                var noOfRoomsStr = request.NoOfRooms;
                var scopeFactory = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        DateTime parsedCheckIn = DateTime.TryParse(checkInStr, out var cin) ? cin : DateTime.UtcNow.AddDays(10);
                        DateTime parsedCheckOut = DateTime.TryParse(checkOutStr, out var cout) ? cout : parsedCheckIn.AddDays(1);
                        int adults = roomGuests?.Sum(rg => int.TryParse(rg.NoOfAdults, out var a) ? a : 1) ?? 1;
                        int rooms = roomGuests != null && roomGuests.Count > 0 ? roomGuests.Count : (int.TryParse(noOfRoomsStr, out var r) ? r : 1);

                        var searchLog = new HotelSearchLog
                        {
                            SearchQuery = cityId,
                            CheckInDate = DateOnly.FromDateTime(parsedCheckIn),
                            CheckOutDate = DateOnly.FromDateTime(parsedCheckOut),
                            Adults = adults,
                            Rooms = rooms,
                            UserId = userId,
                            SearchedAtUtc = DateTime.UtcNow
                        };
                        dbContext.HotelSearchLogs.Add(searchLog);
                        await dbContext.SaveChangesAsync();
                    }
                    catch
                    {
                        // Background task exceptions intentionally silenced
                    }
                });

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel search for CityId {CityId}", request.CityId);
                return StatusCode(500, new { message = $"SRDV Provider failure: {ex.Message}" });
            }
        }

        [HttpPost("recheck-search")]
        [AllowAnonymous]
        public async Task<IActionResult> RecheckSearch([FromBody] SrdvHotelRecheckRequestDto request)
        {
            if (request == null || request.TraceId <= 0)
            {
                return BadRequest(new { message = "A valid positive TraceId is required." });
            }

            try
            {
                var response = await _hotelService.RecheckSearchAsync(request.TraceId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during RecheckSearch for TraceId {TraceId}", request.TraceId);
                return StatusCode(500, new { message = $"SRDV Provider failure: {ex.Message}" });
            }
        }



        // =====================================
        // GET OFFER DETAILS
        // =====================================
        [HttpGet("offers/{offerId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetOfferDetails(string offerId)
        {
            _logger.LogInformation("Fetch offer details request received: OfferId: {OfferId}", offerId);

            if (string.IsNullOrWhiteSpace(offerId))
            {
                return BadRequest(new { message = "offerId is required." });
            }

            try
            {
                var offer = await _hotelService.GetOfferDetailsAsync(offerId.Trim());
                if (offer == null)
                {
                    return NotFound(new { message = "Hotel offer not found or has expired." });
                }
                return Ok(offer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during offer retrieval for OfferId {OfferId}", offerId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================================
        // GET HOTEL INFO
        // =====================================
        [HttpGet("GetHotelInfo")]
        [HttpGet("info")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> GetHotelInfo([FromQuery] string traceId, [FromQuery] string resultIndex, [FromQuery] string hotelCode = "", [FromQuery] string srdvType = "", [FromQuery] string srdvIndex = "", [FromQuery] string endUserIp = "", [FromQuery] string clientId = "", [FromQuery] string userName = "", [FromQuery] string password = "")
        {
            _logger.LogInformation("Fetch hotel info GET request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}, HotelCode: {HotelCode}", traceId, resultIndex, hotelCode);

            if (string.IsNullOrWhiteSpace(traceId) || !long.TryParse(traceId.Trim(), out var tid) || tid <= 0)
            {
                return BadRequest(new { message = "traceId is required and must be a positive integer." });
            }

            var trimmedResultIndex = resultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedResultIndex) || trimmedResultIndex.Length < 3 || trimmedResultIndex.Length > 500)
            {
                return BadRequest(new { message = "resultIndex is required and must be between 3 and 500 characters." });
            }

            try
            {
                var req = new HotelInfoRequestDto
                {
                    TraceId = tid,
                    ResultIndex = trimmedResultIndex,
                    HotelCode = hotelCode?.Trim() ?? "",
                    SrdvType = srdvType?.Trim() ?? "",
                    SrdvIndex = srdvIndex?.Trim() ?? "",
                    EndUserIp = endUserIp?.Trim() ?? "",
                    ClientId = clientId?.Trim() ?? "",
                    UserName = userName?.Trim() ?? "",
                    Password = password?.Trim() ?? ""
                };
                var details = await _hotelService.GetHotelInfoAsync(req);
                return Ok(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel info retrieval for TraceId {TraceId}, ResultIndex {ResultIndex}", traceId, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("GetHotelInfo")]
        [HttpPost("info")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> PostHotelInfo([FromBody] HotelInfoRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Invalid request format." });
            }

            if (request.TraceId == null || !long.TryParse(request.TraceId.ToString(), out var tid) || tid <= 0)
            {
                return BadRequest(new { message = "TraceId is required and must be a positive integer." });
            }

            var resultIndex = request.ResultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(resultIndex) || resultIndex.Length < 3 || resultIndex.Length > 500)
            {
                return BadRequest(new { message = "ResultIndex is required and must be between 3 and 500 characters." });
            }

            _logger.LogInformation("Fetch hotel info POST request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}, HotelCode: {HotelCode}", tid, resultIndex, request.HotelCode);

            try
            {
                var details = await _hotelService.GetHotelInfoAsync(request);
                return Ok(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel info retrieval for TraceId {TraceId}, ResultIndex {ResultIndex}", tid, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================================
        // GET HOTEL ROOMS
        // =====================================
        [HttpGet("rooms")]
        [HttpGet("GetHotelRoom")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> GetHotelRooms([FromQuery] long traceId, [FromQuery] string resultIndex)
        {
            _logger.LogInformation("Fetch hotel rooms GET request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}", traceId, resultIndex);

            if (traceId <= 0)
            {
                return BadRequest(new { message = "traceId is required and must be a positive integer." });
            }

            var trimmedResultIndex = resultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedResultIndex) || trimmedResultIndex.Length < 3 || trimmedResultIndex.Length > 500)
            {
                return BadRequest(new { message = "resultIndex is required and must be between 3 and 500 characters." });
            }

            try
            {
                var rooms = await _hotelService.GetHotelRoomAsync(traceId, trimmedResultIndex);
                return Ok(rooms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel rooms retrieval for TraceId {TraceId}, ResultIndex {ResultIndex}", traceId, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("GetHotelRoom")]
        [HttpPost("rooms")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> PostHotelRooms([FromBody] HotelRoomRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Invalid request format." });
            }

            if (request.TraceId <= 0)
            {
                return BadRequest(new { message = "TraceId is required and must be a positive integer." });
            }

            var resultIndex = request.ResultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(resultIndex) || resultIndex.Length < 3 || resultIndex.Length > 500)
            {
                return BadRequest(new { message = "ResultIndex is required and must be between 3 and 500 characters." });
            }

            _logger.LogInformation("Fetch hotel rooms POST request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}", request.TraceId, resultIndex);

            try
            {
                var rooms = await _hotelService.GetHotelRoomAsync(request);
                return Ok(rooms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel rooms retrieval for TraceId {TraceId}, ResultIndex {ResultIndex}", request.TraceId, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================================
        // BLOCK ROOM
        // =====================================
        [HttpPost("BlockRoom")]
        [HttpPost("block-room")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> PostBlockRoom([FromBody] BlockRoomRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Invalid request format." });
            }

            if (request.TraceId <= 0)
            {
                return BadRequest(new { message = "TraceId is required and must be a positive integer." });
            }

            var resultIndex = request.ResultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(resultIndex) || resultIndex.Length < 3 || resultIndex.Length > 500)
            {
                return BadRequest(new { message = "ResultIndex is required and must be between 3 and 500 characters." });
            }

            // If flat single-room properties are supplied, populate HotelRoomsDetails
            if ((request.HotelRoomsDetails == null || request.HotelRoomsDetails.Count == 0) &&
                (!string.IsNullOrWhiteSpace(request.OptionId) || !string.IsNullOrWhiteSpace(request.RoomTypeCode) || !string.IsNullOrWhiteSpace(request.RoomIndex)))
            {
                request.HotelRoomsDetails = new List<BlockRoomRequestRoomDto>
                {
                    new BlockRoomRequestRoomDto
                    {
                        OptionId = request.OptionId?.Trim() ?? "",
                        RoomTypeCode = request.RoomTypeCode?.Trim() ?? "",
                        RoomIndex = request.RoomIndex?.Trim() ?? ""
                    }
                };
            }

            if (request.HotelRoomsDetails == null || request.HotelRoomsDetails.Count == 0 || request.HotelRoomsDetails.Count > 9)
            {
                return BadRequest(new { message = "HotelRoomsDetails is required and must contain between 1 and 9 rooms." });
            }

            foreach (var room in request.HotelRoomsDetails)
            {
                if (string.IsNullOrWhiteSpace(room.OptionId) && string.IsNullOrWhiteSpace(room.RoomTypeCode) && string.IsNullOrWhiteSpace(room.RoomIndex))
                {
                    return BadRequest(new { message = "Each room in HotelRoomsDetails requires at least one identifier (OptionId, RoomTypeCode, or RoomIndex)." });
                }
            }

            _logger.LogInformation("Block room POST request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}, RoomsCount: {Count}", request.TraceId, resultIndex, request.HotelRoomsDetails.Count);

            try
            {
                var blockRes = await _hotelService.BlockRoomAsync(request);
                return Ok(blockRes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during block room for TraceId {TraceId}, ResultIndex {ResultIndex}", request.TraceId, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("BlockRoom")]
        [HttpGet("block-room")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> GetBlockRoom([FromQuery] long traceId, [FromQuery] string resultIndex, [FromQuery] string? optionId = null, [FromQuery] string? roomTypeCode = null, [FromQuery] string? roomIndex = null)
        {
            if (traceId <= 0)
            {
                return BadRequest(new { message = "traceId is required and must be a positive integer." });
            }

            var trimmedResultIndex = resultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedResultIndex) || trimmedResultIndex.Length < 3 || trimmedResultIndex.Length > 500)
            {
                return BadRequest(new { message = "resultIndex is required and must be between 3 and 500 characters." });
            }

            var roomsList = new List<BlockRoomRequestRoomDto>();
            if (!string.IsNullOrWhiteSpace(optionId) || !string.IsNullOrWhiteSpace(roomTypeCode) || !string.IsNullOrWhiteSpace(roomIndex))
            {
                roomsList.Add(new BlockRoomRequestRoomDto
                {
                    OptionId = optionId?.Trim() ?? "",
                    RoomTypeCode = roomTypeCode?.Trim() ?? "",
                    RoomIndex = roomIndex?.Trim() ?? ""
                });
            }

            var req = new BlockRoomRequestDto
            {
                TraceId = traceId,
                ResultIndex = trimmedResultIndex,
                HotelRoomsDetails = roomsList,
                OptionId = optionId,
                RoomTypeCode = roomTypeCode,
                RoomIndex = roomIndex
            };

            try
            {
                var blockRes = await _hotelService.BlockRoomAsync(req);
                return Ok(blockRes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during block room for TraceId {TraceId}, ResultIndex {ResultIndex}", traceId, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================================
        // =====================================
        // BOOK ROOM (SRDV v8 INTEGRATION)
        // =====================================
        [HttpPost("BookRoom")]
        [HttpPost("book-room")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> PostBookRoom([FromBody] HotelBookRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Invalid request payload." });
            }

            if (request.TraceId <= 0)
            {
                return BadRequest(new { message = "TraceId is required and must be a positive integer." });
            }

            var resultIndex = request.ResultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(resultIndex) || resultIndex.Length < 3 || resultIndex.Length > 500)
            {
                return BadRequest(new { message = "ResultIndex is required and must be between 3 and 500 characters." });
            }

            // If flat passenger fields provided, auto-populate HotelRoomsDetails
            if ((request.HotelRoomsDetails == null || request.HotelRoomsDetails.Count == 0) &&
                (!string.IsNullOrWhiteSpace(request.GuestName) || !string.IsNullOrWhiteSpace(request.GuestEmail)))
            {
                var nameParts = (request.GuestName ?? "Guest User").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string fName = nameParts.Length > 0 ? nameParts[0] : "Guest";
                string lName = nameParts.Length > 1 ? nameParts[1] : "User";

                request.HotelRoomsDetails = new List<BookRoomDetailItemDto>
                {
                    new BookRoomDetailItemDto
                    {
                        HotelPassenger = new List<HotelPassengerDto>
                        {
                            new HotelPassengerDto
                            {
                                Title = "Mr",
                                FirstName = fName,
                                LastName = lName,
                                Email = request.GuestEmail ?? "",
                                Phoneno = request.GuestPhone ?? "",
                                PAN = request.PAN ?? "",
                                LeadPassenger = true,
                                PaxType = "1"
                            }
                        }
                    }
                };
            }

            if (request.HotelRoomsDetails == null || request.HotelRoomsDetails.Count == 0 || request.HotelRoomsDetails.Count > 9)
            {
                return BadRequest(new { message = "HotelRoomsDetails is required and must contain between 1 and 9 rooms." });
            }

            bool hasPassengers = request.HotelRoomsDetails.Any(r => r.HotelPassenger != null && r.HotelPassenger.Count > 0);
            if (!hasPassengers)
            {
                return BadRequest(new { message = "At least one room must carry passengers." });
            }

            foreach (var r in request.HotelRoomsDetails)
            {
                if (r.HotelPassenger != null && r.HotelPassenger.Count > 12)
                {
                    return BadRequest(new { message = "Maximum 12 passengers allowed per room." });
                }
            }

            var leadPax = request.HotelRoomsDetails.SelectMany(r => r.HotelPassenger ?? new List<HotelPassengerDto>())
                .FirstOrDefault(p => p.LeadPassenger) 
                ?? request.HotelRoomsDetails.SelectMany(r => r.HotelPassenger ?? new List<HotelPassengerDto>()).FirstOrDefault();

            string guestName = leadPax != null ? $"{leadPax.Title} {leadPax.FirstName} {leadPax.LastName}".Trim() : (request.GuestName ?? "Guest User");
            string guestEmail = !string.IsNullOrWhiteSpace(leadPax?.Email) ? leadPax.Email : (request.GuestEmail ?? "");
            string guestPhone = !string.IsNullOrWhiteSpace(leadPax?.Phoneno) ? leadPax.Phoneno : (request.GuestPhone ?? "");

            _logger.LogInformation("Book room POST request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}, Guest: {GuestName}", request.TraceId, resultIndex, guestName);

            string userId = _currentUserService.GetUserOrGuestId();
            if (string.IsNullOrWhiteSpace(userId)) userId = "guest_user";

            // Lookup locked pre-blocked price from HotelBlockedPrices table established by BlockRoom
            var traceIdStr = request.TraceId.ToString();
            var blockedPrice = await _dbContext.HotelBlockedPrices
                .FirstOrDefaultAsync(h => h.ResultIndex == resultIndex && h.TraceId == traceIdStr);

            decimal quotedPrice = blockedPrice?.OfferedPrice ?? request.Price;
            decimal publishedPrice = blockedPrice?.OfferedPrice ?? request.Price;
            decimal agentMarkupAmount = blockedPrice?.MarkupAmount ?? 0m;
            decimal markedUpPrice = blockedPrice != null ? blockedPrice.GrandTotal : (publishedPrice + agentMarkupAmount);

            if (agentMarkupAmount <= 0 && quotedPrice > 0 && !string.IsNullOrWhiteSpace(request.HotelCode))
            {
                using var markupScope = HttpContext.RequestServices.CreateScope();
                var markupService = markupScope.ServiceProvider.GetService<IHotelMarkupService>();
                if (markupService != null)
                {
                    agentMarkupAmount = await markupService.CalculateMarkupAsync(quotedPrice, null, request.HotelCode, "B2C");
                    markedUpPrice = publishedPrice + agentMarkupAmount;
                }
            }

            decimal couponDiscount = 0m;
            decimal b2cFinalFare = markedUpPrice;
            HotelCoupon? couponApplied = null;

            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                var validationResult = await ValidateCouponInternalAsync(request.CouponCode, markedUpPrice, userId);
                if (!validationResult.IsValid)
                {
                    return BadRequest(new { message = $"Coupon error: {validationResult.Message}" });
                }
                couponDiscount = validationResult.DiscountAmount;
                couponApplied = validationResult.Coupon;
                b2cFinalFare = markedUpPrice - couponDiscount;
            }

            try
            {
                var bookRes = await _hotelService.BookRoomAsync(request);
                var bRes = bookRes?.BookResult;

                bool isConfirmed = bRes != null && bRes.Error.ErrorCode == 0 &&
                                   (bRes.ResponseStatus == 1 || bRes.Status.Equals("Confirmed", StringComparison.OrdinalIgnoreCase));
                bool isPending = bRes != null && bRes.Error.ErrorCode == 0 &&
                                 (bRes.ResponseStatus == 3 || bRes.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
                bool isSuccess = isConfirmed || isPending;

                // If booking succeeded or is pending with SRDV supplier, save DB reservation record
                if (isSuccess && bRes != null)
                {
                    try
                    {
                        var bookingRef = !string.IsNullOrWhiteSpace(bRes.BookingRefNo) ? bRes.BookingRefNo : $"HT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}";
                        var firstRoom = request.HotelRoomsDetails.FirstOrDefault();
                        string hotelCode = !string.IsNullOrWhiteSpace(request.HotelCode) ? request.HotelCode : (blockedPrice?.HotelCode ?? "HOTEL");
                        string hotelName = !string.IsNullOrWhiteSpace(request.HotelName) ? request.HotelName : hotelCode;

                        var reservation = new HotelReservation
                        {
                            BookingReference = bookingRef.Length > 40 ? bookingRef.Substring(0, 40) : bookingRef,
                            ProviderBookingId = bRes.BookingId > 0 ? bRes.BookingId.ToString() : (!string.IsNullOrWhiteSpace(bRes.BookingRefNo) ? bRes.BookingRefNo : null),
                            SrdvBookingId = bRes.BookingId > 0 ? bRes.BookingId.ToString() : (!string.IsNullOrWhiteSpace(bRes.BookingRefNo) ? bRes.BookingRefNo : null),
                            ConfirmationNo = bRes.ConfirmationNo,
                            InvoiceNumber = bRes.InvoiceNumber,
                            UserId = userId,
                            HotelId = hotelCode.Length > 80 ? hotelCode.Substring(0, 80) : hotelCode,
                            HotelName = hotelName,
                            OfferId = resultIndex,
                            CityCode = string.Empty,
                            TraceId = traceIdStr,
                            GuestName = string.IsNullOrWhiteSpace(guestName) ? "Guest User" : guestName,
                            GuestEmail = string.IsNullOrWhiteSpace(guestEmail) ? "guest@example.com" : guestEmail,
                            GuestPhone = string.IsNullOrWhiteSpace(guestPhone) ? "9876543210" : guestPhone,
                            GuestNationality = "IN",
                            RoomTypeName = request.RoomTypeName ?? firstRoom?.RoomTypeName,
                            RatePlanCode = request.RatePlanCode ?? firstRoom?.RatePlanCode,
                            RoomTypeCode = request.RoomTypeCode ?? firstRoom?.RoomTypeCode ?? "1",
                            CheckInDate = DateTime.TryParse(request.CheckInDate, out var checkIn) ? checkIn : DateTime.UtcNow.AddDays(1),
                            CheckOutDate = DateTime.TryParse(request.CheckOutDate, out var checkOut) ? checkOut : DateTime.UtcNow.AddDays(5),
                            Adults = request.HotelRoomsDetails.Sum(r => r.HotelPassenger?.Count(p => p.PaxType == "1") ?? 1),
                            Children = request.HotelRoomsDetails.Sum(r => r.ChildCount),
                            Rooms = request.HotelRoomsDetails.Count,
                            
                            // Pricing
                            Price = markedUpPrice,
                            SrdvOfferedPrice = quotedPrice,
                            MarkupAmount = agentMarkupAmount,
                            TotalPrice = Math.Max(0m, b2cFinalFare),
                            B2CFinalFare = Math.Max(0m, b2cFinalFare),
                            CouponCode = request.CouponCode,
                            CouponDiscount = couponDiscount,
                            BasePrice = Math.Max(0m, quotedPrice - (blockedPrice?.Tax ?? 0m)),
                            
                            // Tax Breakdown
                            SrdvGstAmount = blockedPrice?.Tax ?? 0m,
                            
                            Status = isPending ? "Pending" : "Confirmed",
                            CreatedAt = DateTime.UtcNow
                        };

                        _dbContext.HotelReservations.Add(reservation);
                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation("Successfully persisted HotelReservation {BookingReference} (ID: {Id}, Status: {Status}) to database.", reservation.BookingReference, reservation.Id, reservation.Status);

                        if (couponApplied != null)
                        {
                            var usage = new HotelCouponUsage
                            {
                                UserId = userId,
                                CouponCode = couponApplied.CouponCode,
                                HotelReservationId = reservation.Id,
                                UsedAtUtc = DateTime.UtcNow,
                                TotalPrice = publishedPrice,
                                DiscountAmount = couponDiscount,
                                CouponType = couponApplied.CouponType,
                                CouponValue = couponApplied.Value,
                                BookingStatus = reservation.Status
                            };
                            _dbContext.HotelCouponUsages.Add(usage);
                            couponApplied.UsedCount += 1;
                            await _dbContext.SaveChangesAsync();
                        }

                        if (isConfirmed)
                        {
                            try
                            {
                                await _ticketEmailService.SendHotelTicketAsync(reservation);
                            }
                            catch (Exception mailEx)
                            {
                                _logger.LogError(mailEx, "Failed to send hotel booking confirmation email for booking {BookingReference}", reservation.BookingReference);
                            }
                        }

                        bookRes.BookResult.FareBreakdown = new FareBreakdownDto
                        {
                            BaseFare = reservation.BasePrice,
                            Markup = reservation.MarkupAmount,
                            Gst = reservation.SrdvGstAmount,
                            Taxes = 0m,
                            TotalPaid = reservation.TotalPrice
                        };
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "Failed to persist HotelReservation record to database during BookRoom: {Msg}", dbEx.Message);
                    }
                }

                return Ok(bookRes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during book room for TraceId {TraceId}, ResultIndex {ResultIndex}", request.TraceId, resultIndex);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("BookRoom")]
        [HttpGet("book-room")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> GetBookRoom([FromQuery] long traceId, [FromQuery] string resultIndex, [FromQuery] string? guestName = null, [FromQuery] string? guestEmail = null, [FromQuery] string? guestPhone = null, [FromQuery] string? pan = null, [FromQuery] string? clientReferenceNo = null)
        {
            _logger.LogInformation("Book room GET request received: TraceId: {TraceId}, ResultIndex: {ResultIndex}, Guest: {GuestName}", traceId, resultIndex, guestName);

            if (traceId <= 0)
            {
                return BadRequest(new { message = "traceId is required and must be a positive integer." });
            }

            var trimmedResultIndex = resultIndex?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedResultIndex) || trimmedResultIndex.Length < 3 || trimmedResultIndex.Length > 500)
            {
                return BadRequest(new { message = "resultIndex is required and must be between 3 and 500 characters." });
            }

            var req = new HotelBookRequestDto
            {
                TraceId = traceId,
                ResultIndex = trimmedResultIndex,
                GuestName = guestName,
                GuestEmail = guestEmail,
                GuestPhone = guestPhone,
                PAN = pan,
                ClientReferenceNo = clientReferenceNo ?? ""
            };

            return await PostBookRoom(req);
        }

        // =====================================
        // BOOKING DETAILS (SRDV v8 INTEGRATION)
        // =====================================
        [HttpPost("BookingDetails")]
        [HttpPost("booking-details")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> PostBookingDetails([FromBody] HotelBookingDetailsRequestDto request)
        {
            if (request == null || request.TraceId <= 0)
            {
                return BadRequest(new { message = "TraceId is required and must be a positive integer." });
            }

            _logger.LogInformation("Hotel BookingDetails POST request received for TraceId {TraceId}", request.TraceId);

            try
            {
                var result = await _hotelService.GetBookingDetailsAsync(request.TraceId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during Hotel BookingDetails for TraceId {TraceId}", request.TraceId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("BookingDetails")]
        [HttpGet("booking-details")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> GetBookingDetails([FromQuery] long traceId)
        {
            if (traceId <= 0)
            {
                return BadRequest(new { message = "traceId is required and must be a positive integer." });
            }

            _logger.LogInformation("Hotel BookingDetails GET request received for TraceId {TraceId}", traceId);

            try
            {
                var result = await _hotelService.GetBookingDetailsAsync(traceId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during Hotel GetBookingDetails for TraceId {TraceId}", traceId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================================
        // HOTEL CANCEL (SRDV v8 INTEGRATION)
        // =====================================
        [HttpPost("Cancel")]
        [HttpPost("cancel-booking")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> PostCancel([FromBody] HotelCancelBookingRequestDto request)
        {
            if (request == null || request.TraceId <= 0)
            {
                return BadRequest(new { message = "TraceId is required and must be a positive integer." });
            }

            _logger.LogInformation("Hotel cancel POST request received for TraceId {TraceId}", request.TraceId);

            try
            {
                var cancelRes = await _hotelService.CancelBookingAsync(request);

                // If acknowledged, send cancellation email
                if (cancelRes.Error?.ErrorCode == 0 && (cancelRes.ResponseStatus == 1 || (cancelRes.ChangeRequestId.HasValue && cancelRes.ChangeRequestId.Value > 0)))
                {
                    try
                    {
                        var traceIdStr = request.TraceId.ToString();
                        var reservation = await _dbContext.HotelReservations.FirstOrDefaultAsync(r => r.TraceId == traceIdStr);
                        if (reservation != null)
                        {
                            await _ticketEmailService.SendHotelCancellationAsync(reservation);
                        }
                    }
                    catch (Exception mailEx)
                    {
                        _logger.LogError(mailEx, "Failed to send hotel cancellation email for TraceId {TraceId}", request.TraceId);
                    }
                }

                if (cancelRes.Error != null && cancelRes.Error.ErrorCode != 0)
                {
                    return StatusCode(502, cancelRes);
                }

                return Ok(cancelRes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel cancel for TraceId {TraceId}", request.TraceId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("Cancel")]
        [HttpGet("cancel-booking")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> GetCancel([FromQuery] long traceId, [FromQuery] string? remarks = null)
        {
            if (traceId <= 0)
            {
                return BadRequest(new { message = "traceId is required and must be a positive integer." });
            }

            var req = new HotelCancelBookingRequestDto
            {
                TraceId = traceId,
                Remarks = !string.IsNullOrWhiteSpace(remarks) ? remarks : "Cancellation requested by guest"
            };

            return await PostCancel(req);
        }

        // =====================================
        // SEND CHANGE REQUEST / HOTEL CANCEL (EXACT MULTI-LEVEL PARITY & MOCK FOR SWAGGER/B2B)
        // =====================================
        [HttpPost("SendChangeRequest")]
        [HttpPost("CancelRoom")]
        [AllowAnonymous]
        [InjectClientIp]
        public async Task<IActionResult> PostSendChangeRequest([FromBody] HotelCancelRequestDto request)
        {
            _logger.LogInformation("Hotel cancel POST request received: BookingId: {BookingId}, RequestType: {RequestType}, TraceId: {TraceId}", request.BookingId, request.RequestType, request.TraceId);

            // If modern TraceId is supplied, route through modern v8 cancellation
            if (!string.IsNullOrWhiteSpace(request.TraceId) && long.TryParse(request.TraceId, out var parsedTid) && parsedTid > 0)
            {
                var v8Req = new HotelCancelBookingRequestDto
                {
                    TraceId = parsedTid,
                    Remarks = request.Remarks,
                    BookingId = request.BookingId,
                    RequestType = request.RequestType,
                    BookingMode = request.BookingMode
                };
                return await PostCancel(v8Req);
            }

            if (request.BookingId <= 0)
            {
                return BadRequest(new { message = "bookingId or traceId is required in the request body." });
            }

            // If only BookingId is provided, attempt to resolve TraceId from DB reservation
            var bookingIdLookup = request.BookingId.ToString();
            var dbReservation = await _dbContext.HotelReservations.FirstOrDefaultAsync(r =>
                r.ProviderBookingId == bookingIdLookup ||
                r.SrdvBookingId == bookingIdLookup ||
                r.Id == request.BookingId);

            if (dbReservation != null && !string.IsNullOrWhiteSpace(dbReservation.TraceId) && long.TryParse(dbReservation.TraceId, out var resTid) && resTid > 0)
            {
                var v8Req = new HotelCancelBookingRequestDto
                {
                    TraceId = resTid,
                    Remarks = request.Remarks,
                    BookingId = request.BookingId
                };
                return await PostCancel(v8Req);
            }

            try
            {
                var cancelRes = await _hotelService.CancelRoomAsync(request);

                // If cancellation was successful on provider side, fetch final refund amount (Step 2) and update DB
                if (cancelRes != null && cancelRes.Error.ErrorCode == 0)
                {
                    if (cancelRes.ChangeRequestId > 0)
                    {
                        var statusReq = new HotelCancelRequestDto
                        {
                            BookingId = request.BookingId,
                            RequestType = 5,
                            ChangeRequestId = cancelRes.ChangeRequestId,
                            BookingMode = request.BookingMode,
                            SrdvType = request.SrdvType,
                            SrdvIndex = request.SrdvIndex,
                            TraceId = request.TraceId, // Although TraceId isn't on HotelCancelRequestDto directly, SrdvType/Index is. Wait, is TraceId on HotelCancelRequestDto?
                            ClientId = request.ClientId,
                            UserName = request.UserName,
                            Password = request.Password,
                            EndUserIp = request.EndUserIp
                        };
                        var statusRes = await _hotelService.CancelRoomAsync(statusReq);
                        if (statusRes != null && statusRes.Error.ErrorCode == 0)
                        {
                            cancelRes.RefundedAmount = statusRes.RefundedAmount;
                            cancelRes.CancellationCharge = statusRes.CancellationCharge;
                            cancelRes.ChangeRequestStatus = statusRes.ChangeRequestStatus;
                        }
                    }

                    var bookingIdStr = request.BookingId.ToString();
                    var reservation = await _dbContext.HotelReservations.FirstOrDefaultAsync(r => 
                        r.ProviderBookingId == bookingIdStr || 
                        r.SrdvBookingId == bookingIdStr ||
                        r.Id == request.BookingId);

                    if (reservation != null)
                    {
                        reservation.Status = "Cancelled";
                        reservation.CancelledAt = DateTime.UtcNow;
                        reservation.UpdatedAt = DateTime.UtcNow;
                        
                        reservation.CancellationCharges = cancelRes.CancellationCharge;

                        decimal calculatedRefund = Math.Max(0m, reservation.TotalPrice - reservation.CancellationCharges - reservation.MarkupAmount - reservation.SrdvGstAmount);
                        if (cancelRes.RefundedAmount > 0)
                        {
                            reservation.RefundAmount = Math.Max(0m, cancelRes.RefundedAmount - reservation.MarkupAmount - reservation.SrdvGstAmount);
                        }
                        else
                        {
                            reservation.RefundAmount = calculatedRefund;
                        }
                        
                        if (!string.IsNullOrWhiteSpace(request.Remarks))
                        {
                            reservation.CancellationReason = request.Remarks;
                        }

                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation("Updated HotelReservation {BookingReference} (ID: {Id}) status to Cancelled in database.", reservation.BookingReference, reservation.Id);

                        try
                        {
                            await _ticketEmailService.SendHotelCancellationAsync(reservation);
                        }
                        catch (Exception mailEx)
                        {
                            _logger.LogError(mailEx, "Failed to send hotel cancellation email for booking {BookingReference}", reservation.BookingReference);
                        }

                        cancelRes.RefundDetails = new RefundDetailsDto
                        {
                            BookingAmount = reservation.TotalPrice,
                            CancellationCharges = reservation.CancellationCharges,
                            RefundAmount = reservation.RefundAmount,
                            RefundStatus = "Initiated"
                        };
                    }
                    else
                    {
                        _logger.LogWarning("HotelReservation record not found in database for BookingId {BookingId}", request.BookingId);
                    }
                }
                else if (cancelRes != null && cancelRes.Error != null && cancelRes.Error.ErrorCode != 0)
                {
                    _logger.LogWarning("Hotel cancel provider failure for BookingId {BookingId}, ErrorCode: {ErrorCode}", request.BookingId, cancelRes.Error.ErrorCode);
                    return StatusCode(502, cancelRes);
                }

                return Ok(cancelRes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during hotel cancel for BookingId {BookingId}", request.BookingId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================================
        // BOOK HOTEL
        // =====================================
        [HttpPost("book")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> Book([FromBody] HotelBookingRequestDto request)
        {
            _logger.LogInformation("Book hotel request received for OfferId: {OfferId}, Guest: {GuestName}", request.OfferId, request.GuestName);

            if (!_currentUserService.IsAuthenticated())
            {
                return Unauthorized(new { message = "Please login to continue booking." });
            }
            var userId = _currentUserService.GetUserOrGuestId();

            if (string.IsNullOrWhiteSpace(request.OfferId) || string.IsNullOrWhiteSpace(request.GuestName) ||
                string.IsNullOrWhiteSpace(request.GuestEmail) || string.IsNullOrWhiteSpace(request.GuestPhone))
            {
                System.IO.File.WriteAllText("book_error.txt", "Required fields missing.");
                return BadRequest(new { message = "OfferId, GuestName, GuestEmail, and GuestPhone are required." });
            }

            // 1. Revalidate and retrieve offer details
            HotelOfferDto? offerDetails;
            try
            {
                _logger.LogDebug("Getting offer details for OfferId: '{OfferId}'", request.OfferId.Trim());
                offerDetails = await _hotelService.GetOfferDetailsAsync(request.OfferId.Trim());
                if (offerDetails == null)
                {
                    _logger.LogDebug("GetOfferDetailsAsync returned NULL for OfferId: '{OfferId}'", request.OfferId.Trim());
                }
                else
                {
                    _logger.LogDebug("GetOfferDetailsAsync returned SUCCESS for OfferId: '{OfferId}'", request.OfferId.Trim());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failure during offer revalidation before booking OfferId: {OfferId}", request.OfferId);
                return StatusCode(500, new { message = $"Unable to revalidate offer with provider: {ex.Message}" });
            }

            // If not in cache, return NotFound directly (no fallback offer construction)
            if (offerDetails == null)
            {
                return NotFound(new
                {
                    message = "Selected offer is no longer available or expired.",
                    hint = "Please search for hotels again (offers are cached for 30 minutes)."
                });
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            try
            {
                return await strategy.ExecuteAsync<IActionResult>(async () =>
                {
                    await using var transaction = await _dbContext.Database.BeginTransactionAsync();

                    // SRDV Supplier Pricing: Direct supplier rate without platform markup, convenience fee, or custom GST
                    decimal srdvOfferedPrice = offerDetails.Price;
                    decimal basePrice = srdvOfferedPrice;
                    decimal netPrice = srdvOfferedPrice;
                    
                    decimal markupAmount = 0m;
                    using var markupScope = HttpContext.RequestServices.CreateScope();
                    var markupService = markupScope.ServiceProvider.GetService<IHotelMarkupService>();
                    if (markupService != null && srdvOfferedPrice > 0)
                    {
                        markupAmount = await markupService.CalculateMarkupAsync(srdvOfferedPrice, offerDetails.CityCode, offerDetails.HotelId, "B2C");
                    }
                    
                    decimal convenienceFee = 0m;
                    decimal totalPrice = srdvOfferedPrice + markupAmount;

                    decimal couponDiscount = 0m;
                    HotelCoupon? couponApplied = null;
                    if (!string.IsNullOrWhiteSpace(request.CouponCode))
                    {
                        var validationResult = await ValidateCouponInternalAsync(request.CouponCode, totalPrice, userId!);
                        if (!validationResult.IsValid)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = $"Coupon error: {validationResult.Message}" });
                        }
                        couponDiscount = validationResult.DiscountAmount;
                        couponApplied = validationResult.Coupon;
                        totalPrice -= couponDiscount;
                        if (totalPrice < 0) totalPrice = 0;
                    }

                    // Round final total price
                    totalPrice = decimal.Round(totalPrice, 2, MidpointRounding.AwayFromZero);

                    // Generate local booking reference
                    var bookingRef = $"HT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}";

                    // Retrieve cached BlockRoom details if present to copy SRDV GST and Cancellation Policies
                    _cache.TryGetValue($"block_{offerDetails.ResultIndex}", out PickNBookBlockRoomResponseDto? cachedBlockRes);
                    var blockResult = cachedBlockRes?.BlockRoomResult;
                    var firstRoom = blockResult?.HotelRoomsDetails?.FirstOrDefault();

                    var reservation = new HotelReservation
                    {
                        BookingReference = bookingRef,
                        UserId = userId!,
                        HotelId = offerDetails.HotelId,
                        HotelName = offerDetails.HotelName,
                        OfferId = offerDetails.OfferId,
                        CityCode = offerDetails.CityCode,
                        GuestName = request.GuestName.Trim(),
                        GuestEmail = request.GuestEmail.Trim(),
                        GuestPhone = request.GuestPhone.Trim(),
                        GuestNationality = "IN",
                        RoomTypeName = offerDetails.RoomCategory,
                        CheckInDate = DateTime.TryParse(offerDetails.CheckInDate, out var checkIn) ? checkIn : DateTime.MinValue,
                        CheckOutDate = DateTime.TryParse(offerDetails.CheckOutDate, out var checkOut) ? checkOut : DateTime.MinValue,
                        Adults = offerDetails.AdultQuantity > 0 ? offerDetails.AdultQuantity : 1,
                        Children = offerDetails.ChildQuantity,
                        Rooms = offerDetails.RoomQuantity,
                        
                        SrdvOfferedPrice = srdvOfferedPrice,
                        Price = srdvOfferedPrice,
                        NetPrice = netPrice,
                        MarkupAmount = markupAmount,
                        BasePrice = basePrice,
                        ConvenienceFee = convenienceFee,
                        TotalPrice = totalPrice,

                        // SRDV Supplier GST Breakdown
                        SrdvGstAmount = firstRoom?.Price?.TotalGSTAmount ?? 0m,
                        SrdvCgstAmount = firstRoom?.Price?.GST?.CGSTAmount ?? 0m,
                        SrdvSgstAmount = firstRoom?.Price?.GST?.SGSTAmount ?? 0m,
                        SrdvIgstAmount = firstRoom?.Price?.GST?.IGSTAmount ?? 0m,

                        // SRDV Supplier Specs & Cancellation Policy
                        RatePlanCode = firstRoom?.RatePlanCode,
                        RoomTypeCode = firstRoom?.RoomTypeCode,
                        LastCancellationDate = DateTime.TryParse(firstRoom?.LastCancellationDate, out var lcd) ? lcd : null,
                        CancellationPolicyJson = firstRoom?.CancellationPolicies != null ? System.Text.Json.JsonSerializer.Serialize(firstRoom.CancellationPolicies) : null,
                        
                        CouponCode = request.CouponCode?.Trim().ToUpperInvariant(),
                        CouponDiscount = couponDiscount,
                        
                        Currency = offerDetails.Currency,
                        Status = "Booked",
                        CreatedAt = DateTime.UtcNow
                    };

                    _dbContext.HotelReservations.Add(reservation);
                    await _dbContext.SaveChangesAsync();

                    // 2. Call SRDV Booking API
                    HotelBookingResponseDto srdvBooking;
                    try
                    {
                        srdvBooking = await _hotelService.BookHotelAsync(
                            offerDetails.OfferId,
                            reservation.GuestName,
                            reservation.GuestEmail,
                            reservation.GuestPhone,
                            userId!);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SRDV booking provider call failed for OfferId {OfferId}", offerDetails.OfferId);
                        // Rollback local db entry
                        await transaction.RollbackAsync();
                        System.IO.File.WriteAllText("book_error.txt", $"Exception: {ex.Message}");
                        return BadRequest(new { message = $"Booking failed at provider: {ex.Message}" });
                    }

                    reservation.ProviderBookingId = srdvBooking.ProviderBookingId;
                    reservation.SrdvBookingId = srdvBooking.ProviderBookingId;
                    reservation.ConfirmationNo = srdvBooking.ConfirmationNo;
                    reservation.InvoiceNumber = srdvBooking.InvoiceNumber;
                    reservation.SrdvBookingResponseJson = System.Text.Json.JsonSerializer.Serialize(srdvBooking);
                    reservation.UpdatedAt = DateTime.UtcNow;

                    bool isProviderError = !string.IsNullOrEmpty(srdvBooking.Error);
                    bool isFailedStatus = srdvBooking.Status != null && (
                        srdvBooking.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                        srdvBooking.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
                        srdvBooking.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                        srdvBooking.Status.Equals("Error", StringComparison.OrdinalIgnoreCase));

                    if (isProviderError || isFailedStatus)
                    {
                        reservation.Status = "Failed";
                        await _dbContext.SaveChangesAsync();
                        await transaction.CommitAsync();
                        var errorMessage = isProviderError ? srdvBooking.Error : $"Provider rejected booking with status: {srdvBooking.Status}";
                        System.IO.File.WriteAllText("book_error.txt", $"Provider Error: {errorMessage}");
                        return BadRequest(new { message = $"Booking failed at provider: {errorMessage}" });
                    }
                    
                    reservation.TraceId = offerDetails.TraceId;
                    reservation.Status = srdvBooking.Status;
                    reservation.UpdatedAt = DateTime.UtcNow;

                    if (srdvBooking.Status == "VerifyPrice")
                    {
                        await _dbContext.SaveChangesAsync();
                        await transaction.CommitAsync();
                        return Ok(new
                        {
                            message = "Price or Cancellation Policy has changed at provider. Please verify the new price before confirming.",
                            requiresPriceVerification = true,
                            bookingDetails = srdvBooking
                        });
                    }

                    if (couponApplied != null)
                    {
                        var usage = new HotelCouponUsage
                        {
                            HotelReservationId = reservation.Id,
                            UserId = userId!,
                            CouponCode = couponApplied.CouponCode,
                            UsedAtUtc = DateTime.UtcNow,
                            TotalPrice = reservation.TotalPrice,
                            CouponType = couponApplied.CouponType,
                            CouponValue = couponApplied.Value,
                            DiscountAmount = couponDiscount,
                            BookingStatus = "Confirmed"
                        };
                        _dbContext.HotelCouponUsages.Add(usage);
                        
                        couponApplied.UsedCount += 1;
                    }

                    await _dbContext.SaveChangesAsync();

                    if (User?.IsInRole(AuthRoles.Agent) == true && string.Equals(request.PaymentMethod, "Agent Wallet", StringComparison.OrdinalIgnoreCase))
                    {
                        var walletService = HttpContext.RequestServices.GetRequiredService<IAgentWalletService>();
                        try
                        {
                            await walletService.DebitWalletForBookingAsync(
                                int.Parse(userId!),
                                totalPrice,
                                bookingRef,
                                "Hotel",
                                $"Hotel Booking - {offerDetails.HotelName} ({offerDetails.CityCode}) - Ref: {bookingRef}"
                            );
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = ex.Message });
                        }
                    }

                    await transaction.CommitAsync();

                    try
                    {
                        await _ticketEmailService.SendHotelTicketAsync(reservation);
                    }
                    catch (Exception mailEx)
                    {
                        _logger.LogError(mailEx, "Failed to send hotel booking confirmation email for reservation {BookingReference}", reservation.BookingReference);
                        System.IO.File.WriteAllText("email_error.txt", $"Email Exception: {mailEx.ToString()}");
                    }

                    var responseDto = new HotelBookingResponseDto
                    {
                        BookingId = "bk-" + reservation.Id,
                        BookingReference = reservation.BookingReference,
                        ProviderBookingId = reservation.ProviderBookingId,
                        HotelId = reservation.HotelId,
                        HotelName = reservation.HotelName,
                        OfferId = reservation.OfferId,
                        UserId = reservation.UserId,
                        GuestName = reservation.GuestName,
                        GuestEmail = reservation.GuestEmail,
                        GuestPhone = reservation.GuestPhone,
                        CheckInDate = reservation.CheckInDate.ToString("yyyy-MM-dd"),
                        CheckOutDate = reservation.CheckOutDate.ToString("yyyy-MM-dd"),
                        Adults = reservation.Adults,
                        Rooms = reservation.Rooms,
                        
                        Price = reservation.Price,
                        NetPrice = reservation.NetPrice,
                        MarkupAmount = reservation.MarkupAmount,
                        BasePrice = reservation.BasePrice,
                        ConvenienceFee = reservation.ConvenienceFee,
                        TotalPrice = reservation.TotalPrice,
                        Amount = reservation.TotalPrice,
                        
                        Currency = reservation.Currency,
                        Status = reservation.Status,
                        CreatedAt = DateTime.SpecifyKind(reservation.CreatedAt, DateTimeKind.Utc)
                    };

                    return CreatedAtAction(nameof(GetOfferDetails), new { offerId = reservation.OfferId }, responseDto);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database failure or unexpected error during booking OfferId: {OfferId}", request.OfferId);
                return StatusCode(500, new { message = "Booking process encountered a database error." });
            }
        }

        // =====================================
        // MY BOOKINGS
        // =====================================
        [HttpGet("my-bookings")]
        [Authorize]
        public async Task<IActionResult> MyBookings()
        {
            if (!_currentUserService.IsAuthenticated())
            {
                return Unauthorized(new { message = "Please login to continue booking." });
            }
            var userId = _currentUserService.GetUserOrGuestId();

            _logger.LogInformation("My Hotel bookings requested for user: {UserId}", userId);

            try
            {
                var bookings = await _dbContext.HotelReservations
                    .Where(x => x.UserId == userId)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => new HotelBookingHistoryDto
                    {
                        BookingId = "bk-" + x.Id,
                        BookingReference = x.BookingReference,
                        HotelId = x.HotelId,
                        HotelName = x.HotelName,
                        Dates = $"{x.CheckInDate:dd MMM yyyy} - {x.CheckOutDate:dd MMM yyyy}",
                        CheckInDate = x.CheckInDate.ToString("yyyy-MM-dd"),
                        CheckOutDate = x.CheckOutDate.ToString("yyyy-MM-dd"),
                        Amount = x.TotalPrice,
                        Status = x.Status,
                        ProviderBookingId = x.ProviderBookingId,
                        TraceId = x.TraceId,
                        GuestName = x.GuestName,
                        CreatedAt = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc)
                    })
                    .ToListAsync();

                return Ok(bookings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve hotel bookings for user {UserId}", userId);
                return StatusCode(500, new { message = "Database error retrieving bookings history." });
            }
        }

        // =====================================
        // CANCEL BOOKING
        // =====================================
        [HttpPost("bookings/{bookingId}/cancel")]
        [Authorize]
        [InjectClientIp]
        public async Task<IActionResult> Cancel(string bookingId, [FromQuery] string? reason)
        {
            _logger.LogInformation("Cancel hotel booking request received: BookingId: {BookingId}, Reason: {Reason}", bookingId, reason);

            if (!_currentUserService.IsAuthenticated())
            {
                return Unauthorized(new { message = "Please login to continue booking." });
            }
            var userId = _currentUserService.GetUserOrGuestId();

            if (string.IsNullOrWhiteSpace(bookingId))
            {
                return BadRequest(new { message = "bookingId is required." });
            }

            int parsedBookingId;
            var rawId = bookingId.Trim();
            if (rawId.StartsWith("bk-", StringComparison.OrdinalIgnoreCase))
            {
                rawId = rawId.Substring(3);
            }
            if (!int.TryParse(rawId, out parsedBookingId))
            {
                return BadRequest(new { message = "Invalid bookingId format." });
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            try
            {
                return await strategy.ExecuteAsync<IActionResult>(async () =>
                {
                    await using var transaction = await _dbContext.Database.BeginTransactionAsync();

                    var booking = await _dbContext.HotelReservations
                        .FirstOrDefaultAsync(x => x.Id == parsedBookingId && x.UserId == userId);

                    if (booking == null)
                    {
                        return NotFound(new { message = "Booking not found." });
                    }

                    if (booking.Status == "Cancelled")
                    {
                        return BadRequest(new { message = "Booking is already cancelled." });
                    }

                    bool providerCancelled = false;
                    if (!string.IsNullOrEmpty(booking.ProviderBookingId))
                    {
                        if (booking.ProviderBookingId.StartsWith("MOCK-BK-", StringComparison.OrdinalIgnoreCase))
                        {
                            providerCancelled = true;
                        }
                        else
                        {
                            var endUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                            providerCancelled = await _hotelService.CancelBookingAsync(booking, endUserIp);
                            if (!providerCancelled)
                            {
                                _logger.LogWarning("Cancellation at provider was unsuccessful for BookingId {BookingId}. Aborting local cancellation.", parsedBookingId);
                                return StatusCode(502, new { message = "Supplier failed to cancel the booking. Please contact support or try again later." });
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(booking.CouponCode))
                    {
                        var usage = await _dbContext.HotelCouponUsages
                            .FirstOrDefaultAsync(u => u.HotelReservationId == parsedBookingId);
                        if (usage != null)
                        {
                            usage.BookingStatus = "Cancelled";
                        }

                        var coupon = await _dbContext.HotelCoupons
                            .FirstOrDefaultAsync(c => c.CouponCode == booking.CouponCode);
                        if (coupon != null && coupon.UsedCount > 0)
                        {
                            coupon.UsedCount -= 1;
                        }
                    }

                    var (supplierCancellationCharge, supplierRefundAmount) = SrdvHotelService.EvaluateCancellationFee(booking);

                    var refundInput = new PickNBook.Api.Models.DTOs.RefundCalculationInput
                    {
                        OriginalCustomerPaid = booking.TotalPrice,
                        SupplierAmount = booking.Price,
                        MarkupAmount = booking.MarkupAmount,
                        DiscountAmount = booking.CouponDiscount,
                        ConvenienceFee = booking.ConvenienceFee,
                        SupplierCancellationCharge = supplierCancellationCharge,
                        SupplierRefundAmount = supplierRefundAmount
                    };

                    var calculatedRefund = _refundCalculator.CalculateCustomerRefund(
                        refundInput);

                    booking.CancellationCharges = calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                    booking.RefundAmount = calculatedRefund.FinalCustomerRefundAmount;
                    booking.Status = "Cancelled";
                    booking.CancelledAt = DateTime.UtcNow;
                    booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason.Trim();
                    booking.UpdatedAt = DateTime.UtcNow;

                    var cancellationAudit = new PickNBook.Api.Models.Entities.BookingCancellation
                    {
                        BookingType = "Hotel",
                        BookingReference = booking.BookingReference,
                        UserId = booking.UserId,
                        CreatedAtUtc = DateTime.UtcNow,
                        OriginalCustomerPaid = booking.TotalPrice,
                        SupplierAmount = booking.Price,
                        MarkupAmount = booking.MarkupAmount,
                        ConvenienceFee = booking.ConvenienceFee,
                        DiscountAmount = booking.CouponDiscount,
                        SupplierRefundAmount = supplierRefundAmount,
                        SupplierCancellationCharge = supplierCancellationCharge,
                        MarkupRefunded = calculatedRefund.MarkupRefunded,
                        FeeRefunded = calculatedRefund.FeeRefunded,
                        CouponForfeited = calculatedRefund.CouponForfeited,
                        CustomerRefundAmount = calculatedRefund.FinalCustomerRefundAmount,
                        Status = "Pending"
                    };
                    
                    _dbContext.BookingCancellations.Add(cancellationAudit);
                    await _dbContext.SaveChangesAsync();

                    if (calculatedRefund.FinalCustomerRefundAmount > 0)
                    {
                        var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.UserId == booking.UserId && p.BookingReferenceId == booking.Id && p.BookingType == "Hotel");
                        if (payment != null && payment.CashfreeOrderId != null)
                        {
                            string refundId = $"REF-CANCEL-{booking.Id}-{cancellationAudit.Id}";
                            await _cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, calculatedRefund.FinalCustomerRefundAmount, refundId, "Hotel Cancellation");
                            cancellationAudit.CashfreeRefundId = refundId;
                            cancellationAudit.Status = "Initiated";
                            cancellationAudit.PaymentId = payment.Id;
                            await _dbContext.SaveChangesAsync();
                        }
                    }

                    await transaction.CommitAsync();

                    try
                    {
                        await _ticketEmailService.SendHotelCancellationAsync(booking);
                    }
                    catch (Exception mailEx)
                    {
                        _logger.LogError(mailEx, "Failed to send hotel booking cancellation email for booking {BookingReference}", booking.BookingReference);
                    }

                    return Ok(new HotelCancellationDto
                    {
                        BookingId = "bk-" + booking.Id,
                        BookingReference = booking.BookingReference,
                        Status = booking.Status,
                        CancelledAt = DateTime.SpecifyKind(booking.CancelledAt.Value, DateTimeKind.Utc),
                        CancellationReason = booking.CancellationReason,
                        Message = "Booking successfully cancelled."
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure during booking cancellation: BookingId {BookingId}", bookingId);
                return StatusCode(500, new { message = "Cancellation process encountered an error." });
            }
        }

        [HttpPost("pricing-preview")]
        [AllowAnonymous]
        public async Task<IActionResult> PricingPreview([FromBody] HotelPricingPreviewRequestDto request)
        {
            _logger.LogInformation("Hotel pricing preview requested for HotelCode: {HotelCode}", request.HotelCode);

            decimal agentMarkup = 0m;
            decimal totalBeforeDiscount = request.B2CBasePrice + request.SrdvGstAmount;
            
            decimal discountAmount = 0m;
            bool isCouponValid = false;
            string couponMessage = string.Empty;

            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                var userId = _currentUserService.GetUserOrGuestId() ?? "Guest";
                var validationResult = await ValidateCouponInternalAsync(request.CouponCode, totalBeforeDiscount, userId);
                
                isCouponValid = validationResult.IsValid;
                couponMessage = validationResult.Message;
                if (validationResult.IsValid)
                {
                    discountAmount = validationResult.DiscountAmount;
                }
            }

            decimal finalTotal = Math.Max(0m, totalBeforeDiscount - discountAmount);
            
            var response = new HotelPricingPreviewResponseDto
            {
                BasePrice = request.B2CBasePrice,
                SrdvGstAmount = request.SrdvGstAmount,
                AgentMarkup = agentMarkup,
                TotalBeforeDiscount = totalBeforeDiscount,
                DiscountAmount = discountAmount,
                FinalTotal = finalTotal,
                IsCouponValid = isCouponValid,
                CouponMessage = couponMessage
            };

            return Ok(response);
        }

        [HttpGet("coupons/active")]
        [AllowAnonymous]
        public async Task<IActionResult> GetActiveCoupons()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var coupons = await _dbContext.HotelCoupons
                .AsNoTracking()
                .Where(c => c.Status == "Active" 
                         && today >= c.StartDate 
                         && today <= c.ExpiryDate
                         && (c.UseLimit == 0 || c.UsedCount < c.UseLimit))
                .Select(c => new 
                {
                    c.CouponCode,
                    c.CouponType,
                    c.Value,
                    c.MinBookingAmount,
                    c.MaxDiscountAmount,
                    c.Remark,
                    c.IsFirstTimeUserOnly
                })
                .ToListAsync();

            return Ok(coupons);
        }

        [HttpPost("coupons/validate")]
        [Authorize]
        public async Task<IActionResult> ValidateCoupon([FromBody] ValidateHotelCouponRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = _currentUserService.GetUserOrGuestId() ?? "Guest";
            var validationResult = await ValidateCouponInternalAsync(request.CouponCode, request.TotalAmount, userId);

            return Ok(new ValidateHotelCouponResponseDto
            {
                IsValid = validationResult.IsValid,
                DiscountAmount = validationResult.DiscountAmount,
                Message = validationResult.Message
            });
        }

        private async Task<(bool IsValid, decimal DiscountAmount, string Message, HotelCoupon? Coupon)> ValidateCouponInternalAsync(string code, decimal totalAmount, string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                var userObj = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id.ToString() == userId);
                if (userObj != null && userObj.Role == AuthRoles.Agent)
                {
                    return (false, 0, "Coupons are not valid for B2B Agents.", null);
                }
            }

            var normalized = code.Trim().ToUpperInvariant();
            var coupon = await _dbContext.HotelCoupons.FirstOrDefaultAsync(c => c.CouponCode == normalized);
            if (coupon == null)
            {
                return (false, 0, "Coupon code not found.", null);
            }

            if (coupon.Status != "Active")
            {
                return (false, 0, "Coupon is inactive.", null);
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today < coupon.StartDate || today > coupon.ExpiryDate)
            {
                return (false, 0, "Coupon has expired.", null);
            }

            if (coupon.UseLimit > 0 && coupon.UsedCount >= coupon.UseLimit)
            {
                return (false, 0, "Coupon usage limit reached.", null);
            }

            if (totalAmount < coupon.MinBookingAmount)
            {
                return (false, 0, $"Minimum booking amount of INR {coupon.MinBookingAmount} is required.", null);
            }

            var userUsageCount = await _dbContext.HotelCouponUsages
                .CountAsync(u => u.CouponCode == normalized && u.UserId == userId && u.BookingStatus != "Cancelled");

            if (userUsageCount >= coupon.MaxUsagePerUser)
            {
                return (false, 0, $"You have exceeded the maximum usage limit of {coupon.MaxUsagePerUser} times for this coupon.", null);
            }

            if (coupon.IsFirstTimeUserOnly)
            {
                var hasPriorBookings = await _dbContext.HotelReservations
                    .AnyAsync(r => r.UserId == userId && r.Status != "Cancelled");
                if (hasPriorBookings)
                {
                    return (false, 0, "This coupon is only valid for your first hotel booking.", null);
                }
            }

            decimal discount = 0;
            if (coupon.CouponType == "Percentage")
            {
                discount = totalAmount * (coupon.Value / 100m);
                if (coupon.MaxDiscountAmount > 0 && discount > coupon.MaxDiscountAmount)
                {
                    discount = coupon.MaxDiscountAmount;
                }
            }
            else if (coupon.CouponType == "Flat")
            {
                discount = coupon.Value;
            }

            discount = Math.Min(discount, totalAmount);
            discount = decimal.Round(discount, 2, MidpointRounding.AwayFromZero);

            return (true, discount, "Coupon is valid.", coupon);
        }
    }
}

