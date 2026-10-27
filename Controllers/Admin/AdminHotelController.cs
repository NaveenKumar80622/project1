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

using PickNBook.Api.Helpers;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Extensions;

namespace PickNBook.Api.Controllers;

[Route("api/admin/hotel")]
public class AdminHotelController : AdminApiController
{
    private readonly AppDbContext _context;
    private readonly IHotelService _hotelService;
    private readonly ILogger<AdminHotelController> _logger;
    private readonly PickNBook.Api.Services.Notifications.Interfaces.INotificationService _notificationService;
    private readonly ITicketPdfService _ticketPdfService;
    private readonly ITicketEmailService _ticketEmailService;

    public AdminHotelController(
        AppDbContext context, 
        IHotelService hotelService, 
        ILogger<AdminHotelController> logger,
        PickNBook.Api.Services.Notifications.Interfaces.INotificationService notificationService,
        ITicketPdfService ticketPdfService,
        ITicketEmailService ticketEmailService)
    {
        _context = context;
        _hotelService = hotelService;
        _logger = logger;
        _notificationService = notificationService;
        _ticketPdfService = ticketPdfService;
        _ticketEmailService = ticketEmailService;
    }

    // 1. List All Bookings: GET /api/admin/hotel/bookings
    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings([FromQuery] string? passengerPhone, [FromQuery] string? status)
    {
        _logger.LogInformation("Retrieving all hotel bookings for admin. Phone: {Phone}, Status: {Status}", passengerPhone, status);
        
        try
        {
            var query = _context.HotelReservations.AsQueryable();

            // Exclude cancelled bookings at DB query level (only active bookings should be fetched)
            query = query.Where(b => b.CancelledAt == null &&
                                     b.Status != "Cancelled" &&
                                     b.Status != "CancellationPending" &&
                                     b.Status != "PartiallyCancelled" &&
                                     b.Status != "Partially Cancelled" &&
                                     (b.Status == null || !b.Status.Contains("Cancel")));

            if (!string.IsNullOrWhiteSpace(passengerPhone))
            {
                query = query.Where(b => b.GuestPhone.Contains(passengerPhone.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(status) && !status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var s = status.Trim();
                if (s.Equals("Booked", StringComparison.OrdinalIgnoreCase) || s.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) || s.Equals("Success", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(b => b.Status == "Booked" || b.Status == "Confirmed" || b.Status == "SUCCESS" || b.Status == "Success");
                }
                else if (!s.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(b => b.Status == s);
                }
            }


            var queryResult = await query
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new
                {
                    b.Id,
                    b.BookingReference,
                    b.ProviderBookingId,
                    b.ConfirmationNo,
                    b.LastCancellationDate,
                    b.SrdvOfferedPrice,
                    b.NetPrice,
                    b.BasePrice,
                    b.MarkupAmount,
                    b.CouponDiscount,
                    b.ConvenienceFee,
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
                    b.CreatedAt,
                    b.CancelledAt,
                    b.CancellationReason,
                    b.CancellationCharges,
                    b.RefundAmount
                })
                .ToListAsync();

            var bookingIds = queryResult.Select(b => b.Id).ToList();
            var bookingRefs = queryResult.Select(b => b.BookingReference).Where(r => !string.IsNullOrEmpty(r)).ToList();

            var payments = await _context.Payments
                .AsNoTracking()
                .Where(p => (p.BookingType == "Hotel" || p.BookingType.ToLower() == "hotel") &&
                            ((p.BookingReferenceId != null && bookingIds.Contains(p.BookingReferenceId.Value)) ||
                             (p.BookingId != null && bookingIds.Contains(p.BookingId.Value)) ||
                             (p.PaymentReference != null && bookingRefs.Contains(p.PaymentReference))))
                .ToListAsync();

            var paymentMapByRefId = payments
                .Where(p => p.BookingReferenceId.HasValue)
                .GroupBy(p => p.BookingReferenceId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

            var paymentMapById = payments
                .Where(p => p.BookingId.HasValue)
                .GroupBy(p => p.BookingId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

            var paymentMapByPaymentRef = payments
                .Where(p => !string.IsNullOrEmpty(p.PaymentReference))
                .GroupBy(p => p.PaymentReference!)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

            var list = queryResult.Select(b => 
            {
                PickNBook.Api.Models.Payments.Payment? payment = null;
                if (!paymentMapByRefId.TryGetValue(b.Id, out payment))
                {
                    if (!paymentMapById.TryGetValue(b.Id, out payment))
                    {
                        if (!string.IsNullOrEmpty(b.BookingReference))
                        {
                            paymentMapByPaymentRef.TryGetValue(b.BookingReference, out payment);
                        }
                    }
                }

                return new
                {
                    BookingId = !string.IsNullOrEmpty(b.ProviderBookingId) ? b.ProviderBookingId : b.Id.ToString(),
                    InternalBookingId = b.Id,
                    BookingReference = b.BookingReference ?? "",
                    ProviderBookingId = b.ProviderBookingId ?? "",
                    ConfirmationNo = b.ConfirmationNo ?? "",
                    LastCancellationDate = b.LastCancellationDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    SrdvOfferedPrice = b.SrdvOfferedPrice,
                    NetFare = b.NetPrice > 0 ? b.NetPrice : (b.SrdvOfferedPrice > 0 ? b.SrdvOfferedPrice : b.TotalPrice),
                    BaseFare = b.BasePrice > 0 ? b.BasePrice : b.TotalPrice,
                    MarkupAmount = b.MarkupAmount,
                    DiscountAmount = b.CouponDiscount,
                    ConvenienceFee = b.ConvenienceFee,
                    Profit = b.TotalPrice - (b.NetPrice > 0 ? b.NetPrice : (b.SrdvOfferedPrice > 0 ? b.SrdvOfferedPrice : b.TotalPrice)),
                    CalculatedProfit = b.TotalPrice - (b.NetPrice > 0 ? b.NetPrice : (b.SrdvOfferedPrice > 0 ? b.SrdvOfferedPrice : b.TotalPrice)),
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
                    Status = BookingStatusResolver.ResolveStatus(
                        b.Status,
                        payment?.Status,
                        payment?.FulfillmentStatus,
                        b.CheckOutDate),
                    PaymentId = payment?.Id,
                    PaymentReference = payment?.PaymentReference,
                    PaymentMethod = payment?.PaymentMethod,
                    CashfreePaymentId = payment?.CashfreePaymentId,
                    CashfreeOrderId = payment?.CashfreeOrderId,
                    PaymentStatus = payment?.Status,
                    RefundStatus = payment?.RefundStatus,
                    FulfillmentStatus = payment?.FulfillmentStatus,
                    CreatedAt = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
                    CreatedAtIst = IndiaTimeHelper.ToIst(b.CreatedAt),
                    BookedAtDisplay = IndiaTimeHelper.FormatIst(b.CreatedAt, "dd MMM yyyy, hh:mm tt"),
                    CancelledAt = b.CancelledAt.HasValue ? DateTime.SpecifyKind(b.CancelledAt.Value, DateTimeKind.Utc) : (DateTime?)null,
                    CancelledAtIst = b.CancelledAt.HasValue ? IndiaTimeHelper.ToIst(b.CancelledAt.Value) : (DateTime?)null,
                    CancelledAtDisplay = b.CancelledAt.HasValue ? IndiaTimeHelper.FormatIst(b.CancelledAt.Value, "dd MMM yyyy, hh:mm tt") : null,
                    CancellationReason = b.CancellationReason,
                    CancellationCharges = b.CancellationCharges,
                    RefundAmount = b.RefundAmount,
                    BookedAt = b.CreatedAt != DateTime.MinValue ? b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss") : ""
                };
            })
            .Where(b => !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                        !b.Status.Contains("Cancel", StringComparison.OrdinalIgnoreCase) &&
                        (!string.IsNullOrWhiteSpace(status) && !status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
                            ? (b.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) ||
                               (status.Trim().Equals("Booked", StringComparison.OrdinalIgnoreCase) && (b.Status == "Booked" || b.Status == "Confirmed" || b.Status == "SUCCESS")) ||
                               (status.Trim().Equals("Confirmed", StringComparison.OrdinalIgnoreCase) && (b.Status == "Booked" || b.Status == "Confirmed" || b.Status == "SUCCESS")))
                            : (b.Status == "Booked" || b.Status == "Confirmed" || b.Status == "SUCCESS")))
            .ToList();

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
            .AsNoTracking()
            .Where(b => b.Status == "Cancelled" || b.Status == "CancellationPending" || b.Status == "PartiallyCancelled" || b.Status == "Partially Cancelled")
            .OrderByDescending(b => b.CancelledAt ?? b.UpdatedAt ?? b.CreatedAt)
            .ToListAsync();

        var bookingReferences = cancellationsDb
            .Select(b => b.BookingReference)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .ToList();

        var bookingCancellations = bookingReferences.Count > 0
            ? await _context.BookingCancellations
                .AsNoTracking()
                .Where(c => c.BookingType == "Hotel" && bookingReferences.Contains(c.BookingReference))
                .OrderByDescending(c => c.CreatedAtUtc)
                .ToListAsync()
            : new List<BookingCancellation>();

        var cancellationLookup = bookingCancellations
            .GroupBy(c => c.BookingReference)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var cancellations = cancellationsDb.Select(b =>
        {
            var cancellationCharge = b.CancellationCharges;
            var refundAmount = b.RefundAmount > 0 ? b.RefundAmount : Math.Max(0m, b.TotalPrice - cancellationCharge);
            var roomType = !string.IsNullOrWhiteSpace(b.RoomTypeName) ? b.RoomTypeName : "Standard Room";

            cancellationLookup.TryGetValue(b.BookingReference, out var cancellation);
            var resolvedRefundStatus = CancellationStatusMapper.ResolveRefundStatus(
                cancellation?.RefundStatus,
                cancellation?.Status,
                refundAmount);

            return new AdminHotelCancellationRequestDto
            {
                Id = b.Id,
                BookingId = b.Id,
                BookingReference = b.BookingReference,
                RequestDateUtc = b.CancelledAt.HasValue ? DateTime.SpecifyKind(b.CancelledAt.Value, DateTimeKind.Utc) : (DateTime?)null,
                RequestDateIst = b.CancelledAt.HasValue ? IndiaTimeHelper.ToIst(b.CancelledAt.Value) : (DateTime?)null,
                RequestDateDisplay = b.CancelledAt.HasValue ? IndiaTimeHelper.FormatIst(b.CancelledAt.Value, "dd MMM yyyy, hh:mm tt") : null,
                HotelName = b.HotelName,
                RoomType = roomType,
                CheckInDate = DateOnly.FromDateTime(b.CheckInDate),
                CheckOutDate = DateOnly.FromDateTime(b.CheckOutDate),
                Customer = b.GuestName,
                CustomerPhone = b.GuestPhone,
                CustomerEmail = b.GuestEmail,
                Status = b.Status,
                CustomerRefundAmountInr = refundAmount,
                AdminRefundAmountInr = refundAmount,
                Remark = b.CancellationReason,
                Details = new AdminHotelCancellationDetailsDto
                {
                    CancellationStatus = b.Status,
                    CustomerRefundStatus = resolvedRefundStatus,
                    AdminRefundStatus = resolvedRefundStatus,
                    CustomerRefundAmountInr = refundAmount,
                    CustomerCancellationChargeInr = cancellationCharge,
                    CustomerServiceChargeInr = 0m,
                    AdminRefundAmountInr = refundAmount,
                    AdminCancellationChargeInr = cancellationCharge,
                    AdminServiceChargeInr = 0m,
                    SupplierRemark = null,
                    CustomerRemark = b.CancellationReason,
                    AdminRemark = null
                }
            };
        }).ToList();

        return Ok(cancellations);
    }

    [HttpPut("cancellations/{id:int}")]
    public async Task<IActionResult> UpdateCancellation(int id, [FromBody] HotelCancellationRequestUpdateDto request)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.CustomerRefundAmountInr < 0 ||
            request.CustomerCancellationChargeInr < 0 ||
            request.CustomerServiceChargeInr < 0 ||
            request.AdminRefundAmountInr < 0 ||
            request.AdminCancellationChargeInr < 0 ||
            request.AdminServiceChargeInr < 0)
        {
            return BadRequest("Refund amounts and fee charges cannot be negative.");
        }

        var row = await _context.HotelReservations.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null)
        {
            return NotFound("Hotel cancellation record not found.");
        }

        row.CancellationCharges = request.CustomerCancellationChargeInr;
        row.RefundAmount = request.CustomerRefundAmountInr;
        if (!string.IsNullOrWhiteSpace(request.AdminRemark))
        {
            row.CancellationReason = request.AdminRemark;
        }

        await _context.SaveChangesAsync();
        return Ok(row);
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

        // Synchronize Payment and BookingCancellation lifecycle
        var payment = await _context.Payments.FirstOrDefaultAsync(p => p.BookingType == "Hotel" && (p.BookingReferenceId == bookingId || p.BookingId == bookingId || (booking.BookingReference != null && p.PaymentReference == booking.BookingReference)));
        if (payment != null)
        {
            payment.RefundStatus = finalRefund > 0 ? "Pending" : "NotRequired";
            payment.RefundReason = booking.CancellationReason;
            payment.UpdatedAt = DateTime.UtcNow;

            var existingCancel = await _context.BookingCancellations.FirstOrDefaultAsync(c => c.PaymentId == payment.Id);
            if (existingCancel == null)
            {
                var newCancel = new PickNBook.Api.Models.Entities.BookingCancellation
                {
                    BookingReference = booking.BookingReference,
                    BookingType = "Hotel",
                    PaymentId = payment.Id,
                    UserId = booking.UserId,
                    OriginalCustomerPaid = payment.FinalPayableAmount,
                    SupplierAmount = booking.NetPrice > 0 ? booking.NetPrice : booking.TotalPrice,
                    SupplierCancellationCharge = finalCharges,
                    SupplierRefundAmount = Math.Max(0m, (booking.NetPrice > 0 ? booking.NetPrice : booking.TotalPrice) - finalCharges),
                    CustomerRefundAmount = finalRefund,
                    SrdvStatus = providerCancelled ? "Success" : "Failed",
                    Status = finalRefund > 0 ? "Pending" : "Completed",
                    RefundStatus = finalRefund > 0 ? "PENDING" : "NOT_REQUIRED",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.BookingCancellations.Add(newCancel);
            }
            else
            {
                existingCancel.SupplierCancellationCharge = finalCharges;
                existingCancel.CustomerRefundAmount = finalRefund;
                existingCancel.SrdvStatus = providerCancelled ? "Success" : "Failed";
                existingCancel.RefundStatus = finalRefund > 0 ? "PENDING" : "NOT_REQUIRED";
            }
        }

        if (!string.IsNullOrWhiteSpace(booking.GuestPhone))
        {
            try
            {
                await _notificationService.EnqueueAsync(
                    eventType: "HotelBookingCancelled",
                    channel: "SMS",
                    recipient: booking.GuestPhone.Trim(),
                    templateKey: "HOTEL_BOOKING_CANCELLED",
                    payload: new
                    {
                        Reference = booking.BookingReference,
                        Status = "Cancelled",
                        Var1 = booking.BookingReference,
                        Var2 = "Cancelled"
                    },
                    bookingId: booking.Id.ToString(),
                    userId: booking.UserId
                );
            }
            catch (Exception smsEx)
            {
                _logger.LogError(smsEx, "Failed to enqueue hotel cancellation SMS for booking {BookingId}", booking.Id);
            }
        }

        await _context.SaveChangesAsync();

        try
        {
            await _ticketEmailService.SendHotelCancellationAsync(booking);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send hotel cancellation email for booking {BookingId}", booking.Id);
        }

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

    // 3b. Single Booking Details: GET /api/admin/hotel/bookings/{id}
    [HttpGet("bookings/{id}")]
    public async Task<IActionResult> GetBookingDetails(string id)
    {
        var cleanId = id?.Trim() ?? string.Empty;
        int.TryParse(cleanId, out int parsedId);

        var booking = await _context.HotelReservations.FirstOrDefaultAsync(b => 
            b.ProviderBookingId == cleanId || 
            b.SrdvBookingId == cleanId || 
            b.BookingReference == cleanId || 
            b.ConfirmationNo == cleanId ||
            (parsedId > 0 && b.Id == parsedId));

        if (booking == null) return NotFound(new { message = "Booking not found." });

        var payment = await _context.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.BookingType == "Hotel" && (p.BookingReferenceId == booking.Id || p.BookingId == booking.Id || (booking.BookingReference != null && p.PaymentReference == booking.BookingReference)));
        var cancellation = await _context.BookingCancellations.AsNoTracking().FirstOrDefaultAsync(c => c.BookingType == "Hotel" && c.BookingReference == booking.BookingReference);

        return Ok(new
        {
            BookingId = !string.IsNullOrEmpty(booking.ProviderBookingId) ? booking.ProviderBookingId : booking.Id.ToString(),
            InternalBookingId = booking.Id,
            booking.Id,
            booking.BookingReference,
            booking.ProviderBookingId,
            booking.ConfirmationNo,
            booking.SrdvOfferedPrice,
            NetFare = booking.NetPrice > 0 ? booking.NetPrice : (booking.SrdvOfferedPrice > 0 ? booking.SrdvOfferedPrice : booking.TotalPrice),
            BaseFare = booking.BasePrice > 0 ? booking.BasePrice : booking.TotalPrice,
            MarkupAmount = booking.MarkupAmount,
            DiscountAmount = booking.CouponDiscount,
            ConvenienceFee = booking.ConvenienceFee,
            Profit = booking.TotalPrice - (booking.NetPrice > 0 ? booking.NetPrice : (booking.SrdvOfferedPrice > 0 ? booking.SrdvOfferedPrice : booking.TotalPrice)),
            CalculatedProfit = booking.TotalPrice - (booking.NetPrice > 0 ? booking.NetPrice : (booking.SrdvOfferedPrice > 0 ? booking.SrdvOfferedPrice : booking.TotalPrice)),
            booking.HotelId,
            booking.HotelName,
            booking.RoomTypeName,
            booking.GuestName,
            booking.GuestEmail,
            booking.GuestPhone,
            booking.CheckInDate,
            booking.CheckOutDate,
            booking.Adults,
            booking.Children,
            booking.Rooms,
            booking.TotalPrice,
            booking.NetPrice,
            booking.Currency,
            booking.Status,
            CancellationPolicy = booking.CancellationPolicyJson,
            CancellationPolicyJson = booking.CancellationPolicyJson,
            EvaluatedCancellationCharges = SrdvHotelService.EvaluateCancellationFee(booking).CancellationCharges,
            booking.CancellationReason,
            booking.CancellationCharges,
            booking.RefundAmount,
            CancelledAt = booking.CancelledAt.HasValue ? DateTime.SpecifyKind(booking.CancelledAt.Value, DateTimeKind.Utc) : (DateTime?)null,
            CancelledAtIst = booking.CancelledAt.HasValue ? IndiaTimeHelper.ToIst(booking.CancelledAt.Value) : (DateTime?)null,
            CancelledAtDisplay = booking.CancelledAt.HasValue ? IndiaTimeHelper.FormatIst(booking.CancelledAt.Value, "dd MMM yyyy, hh:mm tt") : null,
            CreatedAt = DateTime.SpecifyKind(booking.CreatedAt, DateTimeKind.Utc),
            CreatedAtIst = IndiaTimeHelper.ToIst(booking.CreatedAt),
            CreatedAtDisplay = IndiaTimeHelper.FormatIst(booking.CreatedAt, "dd MMM yyyy, hh:mm tt"),
            LastCancellationDate = booking.LastCancellationDate.HasValue ? DateTime.SpecifyKind(booking.LastCancellationDate.Value, DateTimeKind.Utc) : (DateTime?)null,
            LastCancellationDateIst = booking.LastCancellationDate.HasValue ? IndiaTimeHelper.ToIst(booking.LastCancellationDate.Value) : (DateTime?)null,
            LastCancellationDateDisplay = booking.LastCancellationDate.HasValue ? IndiaTimeHelper.FormatIst(booking.LastCancellationDate.Value, "dd MMM yyyy, hh:mm tt") : null,
            PaymentId = payment?.Id,
            PaymentStatus = payment?.Status,
            PaymentReference = payment?.PaymentReference,
            PaymentMethod = payment?.PaymentMethod,
            Payment = payment != null ? new
            {
                payment.Id,
                payment.PaymentReference,
                payment.PaymentMethod,
                payment.Status,
                Amount = payment.TotalAmount,
                payment.FinalPayableAmount,
                payment.RefundStatus,
                payment.RefundReason,
                payment.FulfillmentStatus
            } : null,
            Cancellation = cancellation != null ? new
            {
                cancellation.Id,
                cancellation.Status,
                cancellation.RefundStatus,
                cancellation.CustomerRefundAmount,
                cancellation.SupplierCancellationCharge,
                cancellation.CreatedAtUtc
            } : null
        });
    }

    // 3c. Download PDF Voucher: GET /api/admin/hotel/bookings/{id:int}/voucher
    [HttpGet("bookings/{id:int}/voucher")]
    public async Task<IActionResult> DownloadVoucher(int id)
    {
        var booking = await _context.HotelReservations.FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { message = "Booking not found." });

        try
        {
            var pdfBytes = _ticketPdfService.GenerateHotelTicketPdf(booking);
            var filename = $"hotel-voucher-{(string.IsNullOrWhiteSpace(booking.BookingReference) ? id.ToString() : booking.BookingReference)}.pdf";
            return File(pdfBytes, "application/pdf", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate hotel ticket PDF for booking {Id}", id);
            return StatusCode(500, new { message = "Failed to generate voucher PDF: " + ex.Message });
        }
    }

    // 3d. Resend Confirmation Email: POST /api/admin/hotel/bookings/{id:int}/resend-confirmation-email
    [HttpPost("bookings/{id:int}/resend-confirmation-email")]
    public async Task<IActionResult> ResendConfirmationEmail(int id)
    {
        var booking = await _context.HotelReservations.FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { message = "Booking not found." });

        try
        {
            await _ticketEmailService.SendHotelTicketAsync(booking);
            return Ok(new { message = $"Hotel confirmation voucher sent successfully to {booking.GuestEmail}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send hotel confirmation email for booking {Id}", id);
            return StatusCode(500, new { message = "Failed to send email: " + ex.Message });
        }
    }

    // 4. Search History Logs: GET /api/admin/hotel/search-history
    [HttpGet("search-history")]
    public async Task<IActionResult> GetSearchHistory([FromQuery] string? searchTerm)
    {
        _logger.LogInformation("Retrieving hotel search history for admin. SearchTerm: {SearchTerm}", searchTerm);
        
        var query = _context.HotelSearchLogs.AsQueryable();

        List<long> matchingCityIds = new();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLowerInvariant();
            
            matchingCityIds = await _context.HotelCities.AsNoTracking()
                .Where(c => (c.CityName != null && c.CityName.ToLower().Contains(term)) ||
                            (c.FullName != null && c.FullName.ToLower().Contains(term)))
                .Select(c => c.CityId)
                .ToListAsync();

            var matchingCityIdStrings = matchingCityIds.Select(id => id.ToString()).ToList();

            query = query.Where(s => 
                (s.CityName != null && s.CityName.ToLower().Contains(term)) ||
                s.SearchQuery.ToLower().Contains(term) ||
                (s.CityId.HasValue && matchingCityIds.Contains(s.CityId.Value)) ||
                matchingCityIdStrings.Contains(s.SearchQuery) ||
                (s.UserId != null && s.UserId.ToLower().Contains(term)));
        }

        var logs = await query
            .OrderByDescending(s => s.SearchedAtUtc)
            .ToListAsync();

        var cityIds = logs
            .Select(s => s.CityId ?? (long.TryParse(s.SearchQuery, out var cid) ? cid : (long?)null))
            .Where(cid => cid.HasValue)
            .Select(cid => cid!.Value)
            .Distinct()
            .ToList();

        var cityDict = new Dictionary<long, string>();
        if (cityIds.Count > 0)
        {
            cityDict = await _context.HotelCities.AsNoTracking()
                .Where(c => cityIds.Contains(c.CityId))
                .GroupBy(c => c.CityId)
                .ToDictionaryAsync(g => g.Key, g => g.First().CityName);
        }

        var list = logs.Select(s =>
        {
            long? resolvedId = s.CityId ?? (long.TryParse(s.SearchQuery, out var parsed) ? parsed : null);
            string? displayName = s.CityName;
            if (string.IsNullOrWhiteSpace(displayName) && resolvedId.HasValue && cityDict.TryGetValue(resolvedId.Value, out var matchName))
            {
                displayName = matchName;
            }

            return new
            {
                SearchId = "sh-" + s.Id,
                SearchQuery = displayName ?? s.SearchQuery,
                CityName = displayName ?? s.SearchQuery,
                CityId = resolvedId?.ToString(),
                CheckInDate = s.CheckInDate.ToString("yyyy-MM-dd"),
                CheckOutDate = s.CheckOutDate.ToString("yyyy-MM-dd"),
                Adults = s.Adults,
                Rooms = s.Rooms,
                UserId = s.UserId,
                SearchedAtUtc = DateTime.SpecifyKind(s.SearchedAtUtc, DateTimeKind.Utc)
            };
        }).ToList();

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
    [HttpPost("~/api/admin/hotel/v8/Balance")]
    public async Task<IActionResult> GetBalance([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] BalanceRequestDto? request = null)
    {
        _logger.LogInformation("Admin Hotel Balance POST request received");
        request ??= new BalanceRequestDto();
        if (string.IsNullOrWhiteSpace(request.EndUserIp) || request.EndUserIp == "127.0.0.1")
        {
            request.EndUserIp = HttpContext.GetClientIpAddress();
        }

        var res = await _hotelService.GetBalanceAsync(request);

        if (res.AvailableBalance.HasValue)
        {
            var alertService = HttpContext.RequestServices.GetService<PickNBook.Api.Services.Interfaces.ISrdvWalletAlertService>();
            if (alertService != null)
            {
                _ = alertService.EvaluateAndAlertAsync(res.AvailableBalance.Value);
            }
        }

        if (res.Error != null && res.Error.ErrorCode == 6000)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new
            {
                message = "SRDV master account does not exist or Api-Token is invalid.",
                error = res.Error
            });
        }

        if (res.Error != null && res.Error.ErrorCode != 0)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Failed to fetch SRDV Hotel Balance from upstream supplier.",
                error = res.Error
            });
        }

        return Ok(res);
    }

    [HttpPost("BalanceLog")]
    [HttpPost("~/api/admin/hotel/v8/BalanceLog")]
    public async Task<IActionResult> GetBalanceLog([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] BalanceLogRequestDto? request = null)
    {
        _logger.LogInformation("Admin Hotel BalanceLog POST request received");
        request ??= new BalanceLogRequestDto();
        if (string.IsNullOrWhiteSpace(request.EndUserIp) || request.EndUserIp == "127.0.0.1")
        {
            request.EndUserIp = HttpContext.GetClientIpAddress();
        }

        var res = await _hotelService.GetBalanceLogAsync(request);

        if (res.Error != null && res.Error.ErrorCode == 6000)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new
            {
                message = "SRDV master account does not exist or Api-Token is invalid.",
                error = res.Error
            });
        }

        if (res.Error != null && res.Error.ErrorCode != 0)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Failed to fetch SRDV Hotel BalanceLog from upstream supplier.",
                error = res.Error
            });
        }

        return Ok(res);
    }

    [HttpPost("bookings/{id:int}/resend-confirmation-sms")]
    public async Task<IActionResult> ResendConfirmationSms(int id)
    {
        var reservation = await _context.HotelReservations.FindAsync(id);
        if (reservation == null)
        {
            return NotFound(new { message = $"Hotel reservation {id} not found." });
        }

        if (string.IsNullOrWhiteSpace(reservation.GuestPhone))
        {
            return BadRequest(new { message = "Reservation does not have a valid guest phone number." });
        }

        string checkInFormatted = reservation.CheckInDate.ToString("dd/MM/yyyy");
        string checkOutFormatted = reservation.CheckOutDate.ToString("dd/MM/yyyy");

        var result = await _notificationService.SendImmediateAsync(
            eventType: "HotelBookingSuccess",
            channel: "SMS",
            recipient: reservation.GuestPhone.Trim(),
            templateKey: "HOTEL_BOOKING_CONFIRMED",
            payload: new
            {
                Reference = reservation.BookingReference,
                Hotel = reservation.HotelName,
                HotelName = reservation.HotelName,
                CheckIn = checkInFormatted,
                CheckOut = checkOutFormatted,
                Var1 = reservation.BookingReference,
                Var2 = reservation.HotelName,
                Var3 = checkInFormatted,
                Var4 = checkOutFormatted
            }
        );

        if (!result.IsSuccess)
        {
            return StatusCode(500, new { message = "Failed to send hotel confirmation SMS", error = result.ErrorMessage });
        }

        return Ok(new
        {
            message = "Hotel booking confirmation SMS sent successfully.",
            bookingReference = reservation.BookingReference,
            recipient = reservation.GuestPhone,
            hotel = reservation.HotelName,
            checkIn = checkInFormatted,
            checkOut = checkOutFormatted
        });
    }

    [HttpPost("bookings/{id:int}/send-failure-sms")]
    public async Task<IActionResult> SendFailureSms(int id, [FromQuery] string? reason)
    {
        var reservation = await _context.HotelReservations.FindAsync(id);
        if (reservation == null)
        {
            return NotFound(new { message = $"Hotel reservation {id} not found." });
        }

        if (string.IsNullOrWhiteSpace(reservation.GuestPhone))
        {
            return BadRequest(new { message = "Reservation does not have a valid guest phone number." });
        }

        string failureReason = !string.IsNullOrWhiteSpace(reason) 
            ? reason 
            : (!string.IsNullOrWhiteSpace(reservation.CancellationReason) ? reservation.CancellationReason : "Room unavailable");
        if (failureReason.Length > 45)
        {
            failureReason = failureReason.Substring(0, 42) + "...";
        }

        var result = await _notificationService.SendImmediateAsync(
            eventType: "HotelBookingFailed",
            channel: "SMS",
            recipient: reservation.GuestPhone.Trim(),
            templateKey: "HOTEL_BOOKING_FAILED",
            payload: new
            {
                Reference = reservation.BookingReference,
                Reason = failureReason,
                Var1 = reservation.BookingReference,
                Var2 = failureReason
            }
        );

        if (!result.IsSuccess)
        {
            return StatusCode(500, new { message = "Failed to send hotel failure SMS", error = result.ErrorMessage });
        }

        return Ok(new
        {
            message = "Hotel booking failure SMS sent successfully.",
            bookingReference = reservation.BookingReference,
            recipient = reservation.GuestPhone,
            reason = failureReason
        });
    }

    [HttpPost("bookings/{id:int}/send-checkin-reminder-sms")]
    public async Task<IActionResult> SendCheckInReminderSms(int id)
    {
        var reservation = await _context.HotelReservations.FindAsync(id);
        if (reservation == null)
        {
            return NotFound(new { message = $"Hotel reservation {id} not found." });
        }

        if (string.IsNullOrWhiteSpace(reservation.GuestPhone))
        {
            return BadRequest(new { message = "Reservation does not have a valid guest phone number." });
        }

        string formattedDate = reservation.CheckInDate.ToString("dd/MM/yyyy");
        string hotelName = !string.IsNullOrWhiteSpace(reservation.HotelName) 
            ? reservation.HotelName.Trim() 
            : "Hotel";

        if (hotelName.Length > 40)
        {
            hotelName = hotelName.Substring(0, 37) + "...";
        }

        var result = await _notificationService.SendImmediateAsync(
            eventType: "HotelCheckInReminder",
            channel: "SMS",
            recipient: reservation.GuestPhone.Trim(),
            templateKey: "HOTEL_CHECKIN_REMINDER",
            payload: new
            {
                Hotel = hotelName,
                CheckIn = formattedDate,
                Date = formattedDate,
                Reference = reservation.BookingReference,
                Var1 = hotelName,
                Var2 = formattedDate,
                Var3 = reservation.BookingReference
            }
        );

        if (!result.IsSuccess)
        {
            return StatusCode(500, new { message = "Failed to send hotel check-in reminder SMS", error = result.ErrorMessage });
        }

        return Ok(new
        {
            message = "Hotel check-in reminder SMS sent successfully.",
            bookingReference = reservation.BookingReference,
            recipient = reservation.GuestPhone,
            hotel = hotelName,
            checkInDate = formattedDate
        });
    }
}

public class AdminCancelHotelBookingRequestDto
{
    public string Reason { get; set; } = "Cancelled by admin";
    public decimal CancellationCharges { get; set; } = 0m;
}

