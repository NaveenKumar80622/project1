using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Route("api/admin/hotel")]
public class AdminHotelController : AdminApiController
{
    private readonly AppDbContext _context;
    private readonly IHotelService _hotelService;
    private readonly ILogger<AdminHotelController> _logger;

    public AdminHotelController(AppDbContext context, IHotelService hotelService, ILogger<AdminHotelController> logger)
    {
        _context = context;
        _hotelService = hotelService;
        _logger = logger;
    }

    // 1. List All Bookings: GET /api/admin/hotel/bookings
    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings([FromQuery] string? passengerPhone, [FromQuery] string? status)
    {
        _logger.LogInformation("Retrieving all hotel bookings for admin. Phone: {Phone}, Status: {Status}", passengerPhone, status);
        
        try
        {
            var query = _context.HotelReservations.AsQueryable();

            if (!string.IsNullOrWhiteSpace(passengerPhone))
            {
                query = query.Where(b => b.GuestPhone.Contains(passengerPhone.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(b => b.Status == status.Trim());
            }

            var queryResult = await query
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new
                {
                    b.Id,
                    b.BookingReference,
                    b.ProviderBookingId,
                    b.ConfirmationNo,
                    b.InvoiceNumber,
                    b.LastCancellationDate,
                    b.SrdvOfferedPrice,
                    b.SrdvGstAmount,
                    b.HotelId,
                    b.HotelName,
                    b.GuestName,
                    b.GuestEmail,
                    b.GuestPhone,
                    b.CheckInDate,
                    b.CheckOutDate,
                    b.Adults,
                    b.Children,
                    b.Rooms,
                    b.TotalPrice,
                    b.Currency,
                    b.Status,
                    b.CreatedAt
                })
                .ToListAsync();

            var list = queryResult.Select(b => new
            {
                BookingId = b.Id,
                BookingReference = b.BookingReference ?? "",
                ProviderBookingId = b.ProviderBookingId ?? "",
                ConfirmationNo = b.ConfirmationNo ?? "",
                InvoiceNumber = b.InvoiceNumber ?? "",
                LastCancellationDate = b.LastCancellationDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                SrdvOfferedPrice = b.SrdvOfferedPrice,
                SrdvGstAmount = b.SrdvGstAmount,
                HotelId = b.HotelId ?? "",
                HotelName = b.HotelName ?? "",
                GuestName = b.GuestName ?? "",
                GuestEmail = b.GuestEmail ?? "",
                GuestPhone = b.GuestPhone ?? "",
                CheckInDate = b.CheckInDate != DateTime.MinValue ? b.CheckInDate.ToString("yyyy-MM-dd") : "",
                CheckOutDate = b.CheckOutDate != DateTime.MinValue ? b.CheckOutDate.ToString("yyyy-MM-dd") : "",
                Adults = b.Adults,
                Children = b.Children,
                TotalGuests = b.Adults + b.Children,
                ChildAges = new int[0],
                Rooms = b.Rooms,
                TotalPrice = b.TotalPrice,
                TotalPaid = b.TotalPrice,
                Currency = b.Currency ?? "INR",
                Status = b.Status ?? "Confirmed",
                CreatedAt = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
                BookedAt = b.CreatedAt != DateTime.MinValue ? b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss") : ""
            }).ToList();

            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving admin hotel bookings");
            return StatusCode(500, new { message = $"Failed to retrieve bookings: {ex.Message}" });
        }
    }

    // 2. Cancellation Reports: GET /api/admin/hotel/cancellations
    [HttpGet("cancellations")]
    public async Task<IActionResult> GetCancellations()
    {
        _logger.LogInformation("Retrieving hotel cancellation reports for admin.");

        var cancellationsDb = await _context.HotelReservations
            .Where(b => b.Status == "Cancelled")
            .OrderByDescending(b => b.CancelledAt)
            .Select(b => new
            {
                b.Id,
                b.BookingReference,
                b.HotelName,
                b.GuestName,
                b.GuestPhone,
                b.CreatedAt,
                b.CancelledAt,
                b.CancellationReason,
                b.TotalPrice,
                b.RefundAmount,
                b.CancellationCharges
            })
            .ToListAsync();

        var cancellations = cancellationsDb.Select(b => new
            {
                BookingId = "HB-" + b.Id.ToString("D4"),
                BookingReference = b.BookingReference,
                HotelName = b.HotelName,
                PassengerName = b.GuestName,
                PassengerPhone = b.GuestPhone,
                BookedAtUtc = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
                CancelledAtUtc = b.CancelledAt.HasValue ? DateTime.SpecifyKind(b.CancelledAt.Value, DateTimeKind.Utc) : (DateTime?)null,
                CancellationReason = b.CancellationReason,
                TotalPriceInr = b.TotalPrice,
                RefundAmountInr = b.RefundAmount,
                CancellationChargesInr = b.CancellationCharges,
                Status = "cancelled"
            })
            .ToList();

        return Ok(cancellations);
    }

    // 3. Cancel Booking (Admin Override): POST /api/admin/hotel/bookings/{bookingId}/cancel
    [HttpPost("bookings/{bookingId:int}/cancel")]
    public async Task<IActionResult> CancelBooking(int bookingId, [FromBody] AdminCancelHotelBookingRequestDto request)
    {
        _logger.LogInformation("Admin override cancellation request for BookingId: {BookingId}, Reason: {Reason}", bookingId, request.Reason);

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var booking = await _context.HotelReservations
            .FirstOrDefaultAsync(b => b.Id == bookingId);

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
            try
            {
                providerCancelled = await _hotelService.CancelBookingAsync(booking, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel booking with provider during admin override. ProviderBookingId: {Id}", booking.ProviderBookingId);
            }
        }

        // Release coupon usage if applicable
        if (!string.IsNullOrEmpty(booking.CouponCode))
        {
            var usage = await _context.HotelCouponUsages
                .FirstOrDefaultAsync(u => u.HotelReservationId == bookingId);
            if (usage != null)
            {
                usage.BookingStatus = "Cancelled";
            }

            var coupon = await _context.HotelCoupons
                .FirstOrDefaultAsync(c => c.CouponCode == booking.CouponCode);
            if (coupon != null && coupon.UsedCount > 0)
            {
                coupon.UsedCount -= 1;
            }
        }

        var (evalCharges, evalRefund) = SrdvHotelService.EvaluateCancellationFee(booking);
        decimal finalCharges = request.CancellationCharges > 0 ? request.CancellationCharges : evalCharges;
        decimal finalRefund = Math.Max(0m, booking.TotalPrice - finalCharges);

        booking.Status = "Cancelled";
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancellationReason = string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by admin" : request.Reason.Trim();
        booking.CancellationCharges = finalCharges;
        booking.RefundAmount = finalRefund;
        booking.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            BookingId = booking.Id,
            BookingReference = booking.BookingReference,
            Status = booking.Status,
            CancelledAt = booking.CancelledAt,
            CancellationReason = booking.CancellationReason,
            CancellationCharges = booking.CancellationCharges,
            RefundAmount = booking.RefundAmount,
            Message = providerCancelled 
                ? "Booking cancelled successfully at provider and locally." 
                : "Booking cancelled locally. Provider API cancellation was unavailable or returned error."
        });
    }

    // 4. Search History Logs: GET /api/admin/hotel/search-history
    [HttpGet("search-history")]
    public async Task<IActionResult> GetSearchHistory([FromQuery] string? searchTerm)
    {
        _logger.LogInformation("Retrieving hotel search history for admin. SearchTerm: {SearchTerm}", searchTerm);
        
        var query = _context.HotelSearchLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLowerInvariant();
            query = query.Where(s => s.SearchQuery.ToLower().Contains(term) || (s.UserId != null && s.UserId.ToLower().Contains(term)));
        }

        var list = await query
            .OrderByDescending(s => s.SearchedAtUtc)
            .Select(s => new
            {
                SearchId = "sh-" + s.Id,
                SearchQuery = MapCityCodeToName(s.SearchQuery),
                CheckInDate = s.CheckInDate.ToString("yyyy-MM-dd"),
                CheckOutDate = s.CheckOutDate.ToString("yyyy-MM-dd"),
                Adults = s.Adults,
                Rooms = s.Rooms,
                UserId = s.UserId,
                SearchedAtUtc = DateTime.SpecifyKind(s.SearchedAtUtc, DateTimeKind.Utc)
            })
            .ToListAsync();

        return Ok(list);
    }

    // 5. Query PickNBook Hotel API Balance: GET /api/admin/hotel/picknbook-balance
    [HttpGet("picknbook-balance")]
    public async Task<IActionResult> GetPickNBookBalance()
    {
        _logger.LogInformation("Retrieving PickNBook Hotel API Balance for admin.");
        try
        {
            var endUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1"; var balance = await _hotelService.GetApiBalanceAsync(endUserIp);
            return Ok(balance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve PickNBook Hotel API balance.");
            return StatusCode(500, new { message = $"Failed to retrieve PickNBook Hotel API balance: {ex.Message}" });
        }
    }

    // 6. Query PickNBook Hotel API Balance Log: GET /api/admin/hotel/picknbook-balance-log
    [HttpGet("picknbook-balance-log")]
    public async Task<IActionResult> GetPickNBookBalanceLog()
    {
        _logger.LogInformation("Retrieving PickNBook Hotel API Balance Log for admin.");
        try
        {
            var endUserIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1"; var logs = await _hotelService.GetApiBalanceLogAsync(endUserIp);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve PickNBook Hotel API balance log.");
            return StatusCode(500, new { message = $"Failed to retrieve PickNBook Hotel API balance log: {ex.Message}" });
        }
    }

    private static string MapCityCodeToName(string code)
    {
        var upper = code.Trim().ToUpperInvariant();
        return upper switch
        {
            "DEL" => "New Delhi (DEL)",
            "HYD" => "Hyderabad (HYD)",
            "BOM" => "Mumbai (BOM)",
            "BLR" => "Bengaluru (BLR)",
            "MAA" => "Chennai (MAA)",
            "CCU" => "Kolkata (CCU)",
            "GOI" => "Goa (GOI)",
            "PNQ" => "Pune (PNQ)",
            _ => $"{upper} ({upper})"
        };
    }

    // 7. Hotel Markup Management Endpoints: GET /api/admin/hotel/markup
    [HttpGet("markup")]
    public async Task<IActionResult> GetMarkupRules()
    {
        _logger.LogInformation("Retrieving all hotel markup rules for admin.");
        var rules = await _context.HotelMarkupRules
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new HotelMarkupRuleResponseDto
            {
                Id = r.Id,
                RuleName = r.RuleName,
                CityCode = r.CityCode,
                HotelCode = r.HotelCode,
                UserType = r.UserType,
                MarkupType = r.MarkupType,
                MarkupValue = r.MarkupValue,
                Priority = r.Priority,
                IsActive = r.IsActive,
                CreatedAtUtc = r.CreatedAtUtc,
                UpdatedAtUtc = r.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(rules);
    }

    [HttpGet("markup/{id:int}")]
    public async Task<IActionResult> GetMarkupRuleById(int id)
    {
        var rule = await _context.HotelMarkupRules.FindAsync(id);
        if (rule == null)
            return NotFound(new { message = "Hotel markup rule not found." });

        return Ok(new HotelMarkupRuleResponseDto
        {
            Id = rule.Id,
            RuleName = rule.RuleName,
            CityCode = rule.CityCode,
            HotelCode = rule.HotelCode,
            UserType = rule.UserType,
            MarkupType = rule.MarkupType,
            MarkupValue = rule.MarkupValue,
            Priority = rule.Priority,
            IsActive = rule.IsActive,
            CreatedAtUtc = rule.CreatedAtUtc,
            UpdatedAtUtc = rule.UpdatedAtUtc
        });
    }

    [HttpPost("markup")]
    public async Task<IActionResult> CreateMarkupRule([FromBody] CreateHotelMarkupRuleDto dto)
    {
        if (dto == null)
            return BadRequest(new { message = "Invalid request payload." });

        var rule = new HotelMarkupRule
        {
            RuleName = string.IsNullOrWhiteSpace(dto.RuleName) ? "Hotel Markup Rule" : dto.RuleName.Trim(),
            CityCode = string.IsNullOrWhiteSpace(dto.CityCode) ? "*" : dto.CityCode.Trim().ToUpperInvariant(),
            HotelCode = string.IsNullOrWhiteSpace(dto.HotelCode) ? "*" : dto.HotelCode.Trim(),
            UserType = string.IsNullOrWhiteSpace(dto.UserType) ? "All" : dto.UserType.Trim(),
            MarkupType = string.Equals(dto.MarkupType, "Percentage", StringComparison.OrdinalIgnoreCase) ? "Percentage" : "Flat",
            MarkupValue = dto.MarkupValue,
            Priority = dto.Priority,
            IsActive = dto.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.HotelMarkupRules.Add(rule);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin created hotel markup rule ID {Id}: {RuleName}, {MarkupType} {MarkupValue}", rule.Id, rule.RuleName, rule.MarkupType, rule.MarkupValue);

        return CreatedAtAction(nameof(GetMarkupRuleById), new { id = rule.Id }, new HotelMarkupRuleResponseDto
        {
            Id = rule.Id,
            RuleName = rule.RuleName,
            CityCode = rule.CityCode,
            HotelCode = rule.HotelCode,
            UserType = rule.UserType,
            MarkupType = rule.MarkupType,
            MarkupValue = rule.MarkupValue,
            Priority = rule.Priority,
            IsActive = rule.IsActive,
            CreatedAtUtc = rule.CreatedAtUtc,
            UpdatedAtUtc = rule.UpdatedAtUtc
        });
    }

    [HttpPut("markup/{id:int}")]
    public async Task<IActionResult> UpdateMarkupRule(int id, [FromBody] UpdateHotelMarkupRuleDto dto)
    {
        var rule = await _context.HotelMarkupRules.FindAsync(id);
        if (rule == null)
            return NotFound(new { message = "Hotel markup rule not found." });

        rule.RuleName = string.IsNullOrWhiteSpace(dto.RuleName) ? rule.RuleName : dto.RuleName.Trim();
        rule.CityCode = string.IsNullOrWhiteSpace(dto.CityCode) ? "*" : dto.CityCode.Trim().ToUpperInvariant();
        rule.HotelCode = string.IsNullOrWhiteSpace(dto.HotelCode) ? "*" : dto.HotelCode.Trim();
        rule.UserType = string.IsNullOrWhiteSpace(dto.UserType) ? "All" : dto.UserType.Trim();
        rule.MarkupType = string.Equals(dto.MarkupType, "Percentage", StringComparison.OrdinalIgnoreCase) ? "Percentage" : "Flat";
        rule.MarkupValue = dto.MarkupValue;
        rule.Priority = dto.Priority;
        rule.IsActive = dto.IsActive;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin updated hotel markup rule ID {Id}", id);

        return Ok(new HotelMarkupRuleResponseDto
        {
            Id = rule.Id,
            RuleName = rule.RuleName,
            CityCode = rule.CityCode,
            HotelCode = rule.HotelCode,
            UserType = rule.UserType,
            MarkupType = rule.MarkupType,
            MarkupValue = rule.MarkupValue,
            Priority = rule.Priority,
            IsActive = rule.IsActive,
            CreatedAtUtc = rule.CreatedAtUtc,
            UpdatedAtUtc = rule.UpdatedAtUtc
        });
    }

    [HttpDelete("markup/{id:int}")]
    public async Task<IActionResult> DeleteMarkupRule(int id)
    {
        var rule = await _context.HotelMarkupRules.FindAsync(id);
        if (rule == null)
            return NotFound(new { message = "Hotel markup rule not found." });

        _context.HotelMarkupRules.Remove(rule);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin deleted hotel markup rule ID {Id}", id);

        return Ok(new { message = $"Hotel markup rule {id} deleted successfully." });
    }
    [HttpPost("Balance")]
    public async Task<IActionResult> GetBalance([FromBody] BalanceRequestDto request)
    {
        _logger.LogInformation("Admin Balance POST request received");
        var res = await _hotelService.GetBalanceAsync(request);
        return Ok(res);
    }

    [HttpPost("BalanceLog")]
    public async Task<IActionResult> GetBalanceLog([FromBody] BalanceLogRequestDto request)
    {
        _logger.LogInformation("Admin BalanceLog POST request received");
        var res = await _hotelService.GetBalanceLogAsync(request);
        return Ok(res);
    }
}

public class AdminCancelHotelBookingRequestDto
{
    public string Reason { get; set; } = "Cancelled by admin";
    public decimal CancellationCharges { get; set; } = 0m;
}

