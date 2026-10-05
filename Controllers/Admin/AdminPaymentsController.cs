using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Models.Payments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using PickNBook.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace PickNBook.Api.Controllers.Admin
{
    [Route("api/admin/payments")]
    public class AdminPaymentsController : AdminApiController
    {
        private readonly AppDbContext _dbContext;
        private readonly ICashfreeService _cashfreeService;
        private readonly ILogger<AdminPaymentsController> _logger;

        public AdminPaymentsController(
            AppDbContext dbContext,
            ICashfreeService cashfreeService,
            ILogger<AdminPaymentsController> logger)
        {
            _dbContext = dbContext;
            _cashfreeService = cashfreeService;
            _logger = logger;
        }

        /// <summary>
        /// Get high-level summary metrics for payments dashboard.
        /// </summary>
        [HttpGet("metrics")]
        public async Task<IActionResult> GetPaymentMetrics()
        {
            var payments = await _dbContext.Payments.AsNoTracking().ToListAsync();

            var totalRevenue = payments
                .Where(p => string.Equals(p.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.FinalPayableAmount);

            var totalPayments = payments.Count;

            var successfulPayments = payments.Count(p =>
                string.Equals(p.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));

            var failedPayments = payments.Count(p =>
                string.Equals(p.Status, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Status, "USER_DROPPED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));

            var pendingPayments = payments.Count(p =>
                string.Equals(p.Status, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Status, "CREATED", StringComparison.OrdinalIgnoreCase));

            var pendingRefunds = payments.Count(p =>
                string.Equals(p.RefundStatus, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.RefundStatus, "PROCESSING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.RefundStatus, "RefundProcessing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.RefundStatus, "RefundOnHold", StringComparison.OrdinalIgnoreCase));

            var completedRefunds = payments.Count(p =>
                string.Equals(p.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.RefundStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase));

            var supplierConfirmed = payments.Count(p =>
                string.Equals(p.FulfillmentStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FulfillmentStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase));

            var supplierFailed = payments.Count(p =>
                p.FulfillmentStatus != null && p.FulfillmentStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                success = true,
                data = new
                {
                    totalRevenue = Math.Round(totalRevenue, 2, MidpointRounding.AwayFromZero),
                    totalPayments,
                    successfulPayments,
                    failedPayments,
                    pendingPayments,
                    pendingRefunds,
                    completedRefunds,
                    supplierConfirmed,
                    supplierFailed
                }
            });
        }

        /// <summary>
        /// Get paginated and filtered payments list with complete tracking of Cashfree gateway, SRDV supplier booking, and cancellation/refund.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPayments(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? bookingType = null,
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 100) pageSize = 20;

            var query = _dbContext.Payments.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var sTerm = status.Trim().ToLower();
                query = query.Where(p => p.Status.ToLower() == sTerm ||
                                         (p.FulfillmentStatus != null && p.FulfillmentStatus.ToLower() == sTerm) ||
                                         (p.RefundStatus != null && p.RefundStatus.ToLower() == sTerm));
            }

            if (!string.IsNullOrWhiteSpace(bookingType) && !bookingType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => p.BookingType.ToLower() == bookingType.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(p => p.PaymentReference.ToLower().Contains(s) ||
                                         p.CashfreeOrderId.ToLower().Contains(s) ||
                                         p.UserId.ToLower().Contains(s) ||
                                         (p.CustomerName != null && p.CustomerName.ToLower().Contains(s)) ||
                                         (p.CustomerPhone != null && p.CustomerPhone.Contains(s)) ||
                                         (p.CustomerEmail != null && p.CustomerEmail.ToLower().Contains(s)) ||
                                         (p.CashfreePaymentId != null && p.CashfreePaymentId.ToLower().Contains(s)) ||
                                         (p.RefundId != null && p.RefundId.ToLower().Contains(s)));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(p => p.CreatedAt <= endOfDay);
            }

            var totalRecords = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

            var paymentsPage = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var paymentIds = paymentsPage.Select(p => p.Id).ToList();

            // 1. Batch fetch supplier fulfillment executions
            var executions = await _dbContext.SupplierFulfillmentExecutions
                .AsNoTracking()
                .Where(e => paymentIds.Contains(e.PaymentId))
                .ToListAsync();
            var executionMap = executions
                .GroupBy(e => e.PaymentId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).FirstOrDefault());

            // 2. Batch fetch cancellations
            var cancellations = await _dbContext.BookingCancellations
                .AsNoTracking()
                .Where(c => paymentIds.Contains(c.PaymentId))
                .ToListAsync();
            var cancellationMap = cancellations
                .GroupBy(c => c.PaymentId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).FirstOrDefault());

            // 3. Batch fetch reservations for current page
            var hotelIds = paymentsPage
                .Where(p => string.Equals(p.BookingType, "Hotel", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue)
                .Select(p => p.BookingId!.Value)
                .Distinct()
                .ToList();
            var busIds = paymentsPage
                .Where(p => string.Equals(p.BookingType, "Bus", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue)
                .Select(p => p.BookingId!.Value)
                .Distinct()
                .ToList();
            var flightIds = paymentsPage
                .Where(p => string.Equals(p.BookingType, "Flight", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue)
                .Select(p => p.BookingId!.Value)
                .Distinct()
                .ToList();

            var hotelMap = hotelIds.Any()
                ? await _dbContext.HotelReservations.AsNoTracking()
                    .Where(h => hotelIds.Contains(h.Id))
                    .ToDictionaryAsync(h => h.Id)
                : new Dictionary<int, HotelReservation>();

            var busMap = busIds.Any()
                ? await _dbContext.BusReservations.AsNoTracking()
                    .Include(b => b.BusBooking)
                    .Where(b => busIds.Contains(b.Id))
                    .ToDictionaryAsync(b => b.Id)
                : new Dictionary<int, BusReservation>();

            var flightMap = flightIds.Any()
                ? await _dbContext.FlightReservations.AsNoTracking()
                    .Where(f => flightIds.Contains(f.Id))
                    .ToDictionaryAsync(f => f.Id)
                : new Dictionary<int, FlightReservation>();

            var items = paymentsPage.Select(p =>
            {
                executionMap.TryGetValue(p.Id, out var exec);
                cancellationMap.TryGetValue(p.Id, out var cancel);

                string? bookingRef = null;
                string? pnr = exec?.SupplierReference;
                string srdvStatus = exec?.SupplierBookingStatus ?? p.FulfillmentStatus;
                string summary = p.BookingType;
                bool isCancelled = cancel != null;
                decimal cancelCharges = cancel?.SupplierCancellationCharge ?? 0m;
                decimal refundAmt = cancel?.CustomerRefundAmount ?? (string.Equals(p.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase) ? p.FinalPayableAmount : 0m);

                if (string.Equals(p.BookingType, "Hotel", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue && hotelMap.TryGetValue(p.BookingId.Value, out var hotel))
                {
                    bookingRef = hotel.BookingReference;
                    pnr = !string.IsNullOrWhiteSpace(hotel.ConfirmationNo) ? hotel.ConfirmationNo : (!string.IsNullOrWhiteSpace(hotel.ProviderBookingId) ? hotel.ProviderBookingId : pnr);
                    if (!string.IsNullOrWhiteSpace(hotel.Status)) srdvStatus = hotel.Status;
                    summary = !string.IsNullOrWhiteSpace(hotel.HotelName) ? $"{hotel.HotelName} ({hotel.CheckInDate:dd MMM} - {hotel.CheckOutDate:dd MMM})" : "Hotel Stay";
                    if (string.Equals(hotel.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) isCancelled = true;
                    if (cancelCharges == 0 && hotel.CancellationCharges > 0) cancelCharges = hotel.CancellationCharges;
                    if (refundAmt == 0 && hotel.RefundAmount > 0) refundAmt = hotel.RefundAmount;
                }
                else if (string.Equals(p.BookingType, "Bus", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue && busMap.TryGetValue(p.BookingId.Value, out var bus))
                {
                    bookingRef = bus.BookingReference;
                    pnr = !string.IsNullOrWhiteSpace(bus.Pnr) ? bus.Pnr : pnr;
                    if (!string.IsNullOrWhiteSpace(bus.Status)) srdvStatus = bus.Status;
                    summary = bus.BusBooking != null ? $"{bus.BusBooking.FromCity} → {bus.BusBooking.ToCity} ({bus.BusBooking.OperatorName})" : "Bus Journey";
                    if (string.Equals(bus.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(bus.Status, "Partially Cancelled", StringComparison.OrdinalIgnoreCase)) isCancelled = true;
                    if (cancelCharges == 0 && (bus.CancellationChargeInr ?? 0) > 0) cancelCharges = bus.CancellationChargeInr!.Value;
                    if (refundAmt == 0 && (bus.RefundAmountInr ?? 0) > 0) refundAmt = bus.RefundAmountInr!.Value;
                }
                else if (string.Equals(p.BookingType, "Flight", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue && flightMap.TryGetValue(p.BookingId.Value, out var flight))
                {
                    bookingRef = flight.BookingReference;
                    pnr = !string.IsNullOrWhiteSpace(flight.Pnr) ? flight.Pnr : pnr;
                    if (!string.IsNullOrWhiteSpace(flight.Status)) srdvStatus = flight.Status;
                    summary = !string.IsNullOrWhiteSpace(flight.Airline) ? $"{flight.Airline} ({flight.FromCity} → {flight.ToCity})" : "Flight Journey";
                    if (string.Equals(flight.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) isCancelled = true;
                    if (cancelCharges == 0 && (flight.CancellationChargeInr ?? 0) > 0) cancelCharges = flight.CancellationChargeInr!.Value;
                    if (refundAmt == 0 && (flight.RefundAmountInr ?? 0) > 0) refundAmt = flight.RefundAmountInr!.Value;
                }

                return new
                {
                    p.Id,
                    p.PaymentReference,
                    p.CashfreeOrderId,
                    p.CashfreePaymentId,
                    p.PaymentSessionId,
                    p.UserId,
                    CustomerName = p.CustomerName ?? (p.BookingType == "Hotel" && hotelMap.ContainsKey(p.BookingId ?? 0) ? hotelMap[p.BookingId!.Value].GuestName : null),
                    CustomerEmail = p.CustomerEmail ?? (p.BookingType == "Hotel" && hotelMap.ContainsKey(p.BookingId ?? 0) ? hotelMap[p.BookingId!.Value].GuestEmail : null),
                    CustomerPhone = p.CustomerPhone ?? (p.BookingType == "Hotel" && hotelMap.ContainsKey(p.BookingId ?? 0) ? hotelMap[p.BookingId!.Value].GuestPhone : null),
                    p.PassengerCount,
                    p.BookingType,
                    p.BookingId,
                    BookingReference = bookingRef ?? p.PaymentReference,
                    Pnr = pnr,
                    BookingSummary = summary,
                    p.OriginalAmount,
                    p.MarkupAmount,
                    p.ConvenienceFee,
                    p.DiscountAmount,
                    p.FinalPayableAmount,
                    TotalAmount = p.TotalAmount > 0 ? p.TotalAmount : p.FinalPayableAmount,
                    p.WalletUsedAmount,
                    GatewayPaidAmount = p.GatewayPaidAmount > 0 ? p.GatewayPaidAmount : (p.FinalPayableAmount - p.WalletUsedAmount),
                    PaymentMethod = p.PaymentMethod ?? (p.WalletUsedAmount > 0 && p.GatewayPaidAmount > 0 ? "Hybrid" : p.WalletUsedAmount > 0 ? "Wallet" : "Cashfree"),
                    p.Currency,
                    p.Status,
                    p.FulfillmentStatus,
                    SrdvBookingStatus = srdvStatus,
                    p.RefundStatus,
                    IsCancelled = isCancelled,
                    CancellationCharges = cancelCharges,
                    CustomerRefundAmount = refundAmt,
                    RefundId = p.RefundId ?? cancel?.CashfreeRefundId,
                    p.CreatedAt,
                    p.PaidAt,
                    p.FailureReason,
                    SupplierError = exec?.LastError
                };
            }).ToList();

            return Ok(new
            {
                success = true,
                page,
                pageSize,
                totalRecords,
                totalPages,
                data = items
            });
        }

        /// <summary>
        /// Get single payment breakdown details with full 3-pillar tracking (Gateway, SRDV, Cancellation/Refund) and visual timeline.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetPaymentById(int id)
        {
            var payment = await _dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null)
            {
                return NotFound(new { success = false, message = "Payment record not found." });
            }

            var exec = await _dbContext.SupplierFulfillmentExecutions.AsNoTracking()
                .Where(e => e.PaymentId == id)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            var cancel = await _dbContext.BookingCancellations.AsNoTracking()
                .Where(c => c.PaymentId == id)
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            HotelReservation? hotel = null;
            BusReservation? bus = null;
            FlightReservation? flight = null;

            if (string.Equals(payment.BookingType, "Hotel", StringComparison.OrdinalIgnoreCase) && payment.BookingId.HasValue)
            {
                hotel = await _dbContext.HotelReservations.AsNoTracking().FirstOrDefaultAsync(h => h.Id == payment.BookingId.Value);
            }
            else if (string.Equals(payment.BookingType, "Bus", StringComparison.OrdinalIgnoreCase) && payment.BookingId.HasValue)
            {
                bus = await _dbContext.BusReservations.AsNoTracking().Include(b => b.BusBooking).FirstOrDefaultAsync(b => b.Id == payment.BookingId.Value);
            }
            else if (string.Equals(payment.BookingType, "Flight", StringComparison.OrdinalIgnoreCase) && payment.BookingId.HasValue)
            {
                flight = await _dbContext.FlightReservations.AsNoTracking().FirstOrDefaultAsync(f => f.Id == payment.BookingId.Value);
            }

            string? bookingRef = hotel?.BookingReference ?? bus?.BookingReference ?? flight?.BookingReference ?? payment.PaymentReference;
            string? pnr = hotel?.ConfirmationNo ?? hotel?.ProviderBookingId ?? bus?.Pnr ?? flight?.Pnr ?? exec?.SupplierReference;
            string srdvStatus = hotel?.Status ?? bus?.Status ?? flight?.Status ?? exec?.SupplierBookingStatus ?? payment.FulfillmentStatus;
            string summary = hotel != null ? $"{hotel.HotelName} ({hotel.CheckInDate:dd MMM} - {hotel.CheckOutDate:dd MMM})" :
                             bus != null && bus.BusBooking != null ? $"{bus.BusBooking.FromCity} → {bus.BusBooking.ToCity} ({bus.BusBooking.OperatorName})" :
                             flight != null ? $"{flight.Airline} ({flight.FromCity} → {flight.ToCity})" : payment.BookingType;

            bool isCancelled = cancel != null ||
                               string.Equals(hotel?.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(bus?.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(flight?.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            decimal cancelCharges = cancel?.SupplierCancellationCharge ?? hotel?.CancellationCharges ?? bus?.CancellationChargeInr ?? flight?.CancellationChargeInr ?? 0m;
            decimal refundAmt = cancel?.CustomerRefundAmount ?? hotel?.RefundAmount ?? bus?.RefundAmountInr ?? flight?.RefundAmountInr ?? (string.Equals(payment.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) ? payment.FinalPayableAmount : 0m);

            // Build chronological visual audit timeline
            var timeline = new List<object>();

            // Step 1: Order Created
            timeline.Add(new
            {
                timestamp = payment.CreatedAt,
                stage = "ORDER_CREATED",
                title = "Payment Order Initiated",
                status = "COMPLETED",
                description = $"Cashfree Order #{payment.CashfreeOrderId} created for amount ₹{payment.FinalPayableAmount:F2} ({payment.Currency})."
            });

            // Step 2: Gateway Payment
            if (payment.PaidAt.HasValue || string.Equals(payment.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) || string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                decimal gwPaid = payment.GatewayPaidAmount > 0 ? payment.GatewayPaidAmount : (payment.FinalPayableAmount - payment.WalletUsedAmount);
                timeline.Add(new
                {
                    timestamp = payment.PaidAt ?? payment.UpdatedAt,
                    stage = "PAYMENT_CAPTURED",
                    title = $"Payment Captured ({payment.PaymentMethod ?? "Cashfree"})",
                    status = "COMPLETED",
                    description = $"Payment of ₹{payment.FinalPayableAmount:F2} captured successfully. Cashfree Payment ID: {payment.CashfreePaymentId ?? "Verified"}. Gateway: ₹{gwPaid:F2}, Wallet: ₹{payment.WalletUsedAmount:F2}."
                });
            }
            else if (string.Equals(payment.Status, "FAILED", StringComparison.OrdinalIgnoreCase) || string.Equals(payment.Status, "USER_DROPPED", StringComparison.OrdinalIgnoreCase))
            {
                timeline.Add(new
                {
                    timestamp = payment.UpdatedAt,
                    stage = "PAYMENT_FAILED",
                    title = "Payment Failed / Dropped",
                    status = "FAILED",
                    description = payment.FailureReason ?? payment.LastError ?? "Customer dropped or transaction failed at payment gateway."
                });
            }

            // Step 3: SRDV Fulfillment
            if (exec != null || payment.FulfillmentStatus != "Pending")
            {
                bool isFulfillSuccess = string.Equals(payment.FulfillmentStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(exec?.SupplierBookingStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(exec?.SupplierBookingStatus, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(srdvStatus, "Booked", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(srdvStatus, "Confirmed", StringComparison.OrdinalIgnoreCase);

                timeline.Add(new
                {
                    timestamp = exec?.CreatedAt ?? payment.UpdatedAt,
                    stage = isFulfillSuccess ? "SRDV_CONFIRMED" : "SRDV_DISPATCH",
                    title = isFulfillSuccess ? "SRDV Booking Confirmed" : $"SRDV Fulfillment: {payment.FulfillmentStatus}",
                    status = isFulfillSuccess ? "COMPLETED" : (payment.FulfillmentStatus.StartsWith("Failed") ? "FAILED" : "PROCESSING"),
                    description = isFulfillSuccess
                        ? $"Supplier confirmed reservation #{payment.BookingId}. Provider Reference / PNR: {pnr ?? exec?.SupplierReference ?? "Confirmed"}."
                        : (exec?.LastError ?? payment.LastError ?? "Supplier fulfillment dispatched to SRDV API.")
                });
            }

            // Step 4: Cancellation (if any)
            if (isCancelled)
            {
                DateTime cancelDate = cancel?.CreatedAtUtc ?? hotel?.CancelledAt ?? bus?.CancelledAtUtc ?? flight?.CancelledAtUtc ?? payment.UpdatedAt;
                timeline.Add(new
                {
                    timestamp = cancelDate,
                    stage = "CANCELLATION",
                    title = "Booking Cancelled",
                    status = "COMPLETED",
                    description = $"Booking cancellation recorded. Supplier penalty: ₹{cancelCharges:F2}, Eligible refund: ₹{refundAmt:F2}. Reason: {cancel?.FailureReason ?? hotel?.CancellationReason ?? bus?.CancellationReason ?? flight?.CancellationReason ?? payment.RefundReason ?? "User/Admin requested"}."
                });
            }

            // Step 5: Refund Settlement (if applicable)
            if (string.Equals(payment.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payment.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payment.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cancel?.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                timeline.Add(new
                {
                    timestamp = cancel?.CompletedAtUtc ?? payment.UpdatedAt,
                    stage = "REFUND_COMPLETED",
                    title = "Refund Settled",
                    status = "COMPLETED",
                    description = $"Refund of ₹{(refundAmt > 0 ? refundAmt : payment.FinalPayableAmount):F2} settled. Cashfree Refund ID: {payment.RefundId ?? cancel?.CashfreeRefundId ?? "N/A"}."
                });
            }
            else if (string.Equals(payment.RefundStatus, "RefundOnHold", StringComparison.OrdinalIgnoreCase))
            {
                timeline.Add(new
                {
                    timestamp = payment.UpdatedAt,
                    stage = "REFUND_ONHOLD",
                    title = "Refund On Hold (Gateway Balance)",
                    status = "WARNING",
                    description = "Refund initiated but placed ONHOLD by Cashfree due to insufficient merchant account balance."
                });
            }
            else if (string.Equals(payment.RefundStatus, "RefundProcessing", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(payment.RefundStatus, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(payment.RefundStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                timeline.Add(new
                {
                    timestamp = payment.UpdatedAt,
                    stage = "REFUND_PROCESSING",
                    title = "Refund Processing",
                    status = "PROCESSING",
                    description = $"Refund of ₹{refundAmt:F2} is queued/processing with Cashfree."
                });
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    // Flat fields for backward compatibility
                    payment.Id,
                    payment.PaymentReference,
                    payment.CashfreeOrderId,
                    payment.CashfreeCfOrderId,
                    payment.PaymentSessionId,
                    payment.CashfreePaymentId,
                    payment.UserId,
                    CustomerName = payment.CustomerName ?? hotel?.GuestName ?? bus?.PassengerName ?? flight?.PassengerName,
                    CustomerEmail = payment.CustomerEmail ?? hotel?.GuestEmail ?? bus?.PassengerEmail ?? flight?.PassengerEmail,
                    CustomerPhone = payment.CustomerPhone ?? hotel?.GuestPhone ?? bus?.PassengerPhone ?? flight?.PassengerPhone,
                    payment.PassengerCount,
                    payment.PassengerDetailsJson,
                    payment.BookingType,
                    payment.BookingId,
                    BookingReference = bookingRef,
                    Pnr = pnr,
                    BookingSummary = summary,
                    payment.OriginalAmount,
                    payment.MarkupAmount,
                    payment.ConvenienceFee,
                    payment.DiscountAmount,
                    payment.CouponCode,
                    payment.OfferCode,
                    payment.FinalPayableAmount,
                    TotalAmount = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                    payment.WalletUsedAmount,
                    GatewayPaidAmount = payment.GatewayPaidAmount > 0 ? payment.GatewayPaidAmount : (payment.FinalPayableAmount - payment.WalletUsedAmount),
                    PaymentMethod = payment.PaymentMethod ?? (payment.WalletUsedAmount > 0 && payment.GatewayPaidAmount > 0 ? "Hybrid" : payment.WalletUsedAmount > 0 ? "Wallet" : "Cashfree"),
                    payment.Currency,
                    payment.Status,
                    payment.FulfillmentStatus,
                    SrdvBookingStatus = srdvStatus,
                    payment.RefundStatus,
                    payment.RefundId,
                    payment.RefundReason,
                    payment.RefundAttempts,
                    payment.LastError,
                    payment.FailureReason,
                    payment.CreatedAt,
                    payment.UpdatedAt,
                    payment.PaidAt,
                    payment.WebhookReceivedAt,

                    // Nested 3-Pillar Enterprise Details
                    supplierFulfillment = exec == null ? null : new
                    {
                        exec.Id,
                        exec.SupplierReference,
                        exec.SupplierBookingStatus,
                        exec.ReservationId,
                        exec.LastError,
                        exec.CreatedAt,
                        exec.UpdatedAt
                    },
                    bookingDetails = new
                    {
                        BookingId = payment.BookingId,
                        BookingType = payment.BookingType,
                        BookingReference = bookingRef,
                        Pnr = pnr,
                        Title = summary,
                        Status = srdvStatus,
                        TravelDate = hotel?.CheckInDate.ToString("yyyy-MM-dd") ?? bus?.BusBooking?.DepartureTime.ToString("yyyy-MM-dd") ?? flight?.DepartureTime.ToString("yyyy-MM-dd"),
                        CheckIn = hotel?.CheckInDate,
                        CheckOut = hotel?.CheckOutDate
                    },
                    cancellation = cancel == null && !isCancelled ? null : new
                    {
                        IsCancelled = isCancelled,
                        CancellationCharges = cancelCharges,
                        CustomerRefundAmount = refundAmt,
                        SrdvStatus = cancel?.SrdvStatus ?? (isCancelled ? "Success" : "Pending"),
                        RefundStatus = cancel?.RefundStatus ?? payment.RefundStatus,
                        CashfreeRefundId = cancel?.CashfreeRefundId ?? payment.RefundId,
                        RefundPreference = cancel?.RefundPreference ?? "OriginalMethod",
                        WalletRefundAmount = cancel?.WalletRefundAmount ?? 0m,
                        GatewayRefundAmount = cancel?.GatewayRefundAmount ?? (refundAmt > 0 ? refundAmt : 0m),
                        CreatedAtUtc = cancel?.CreatedAtUtc ?? hotel?.CancelledAt ?? bus?.CancelledAtUtc ?? flight?.CancelledAtUtc,
                        CompletedAtUtc = cancel?.CompletedAtUtc
                    },
                    timeline
                }
            });
        }

        /// <summary>
        /// Dispatch refund request to Cashfree gateway or check live refund status for a payment record.
        /// </summary>
        [HttpPost("{id:int}/refund")]
        public async Task<IActionResult> InitiateRefund(int id, [FromBody] AdminRefundRequestDto req)
        {
            var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null)
            {
                return NotFound(new { success = false, message = "Payment record not found." });
            }

            if (string.Equals(payment.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payment.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { success = false, message = "Payment has already been refunded." });
            }

            if (!string.Equals(payment.Status, "Success", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payment.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { success = false, message = "Cannot refund an unpaid or failed payment." });
            }

            decimal refundAmount = req.RefundAmount.HasValue && req.RefundAmount.Value > 0
                ? req.RefundAmount.Value
                : payment.FinalPayableAmount;

            string refundReason = !string.IsNullOrWhiteSpace(req.RefundReason)
                ? req.RefundReason.Trim()
                : (payment.RefundReason ?? "Admin initiated refund");

            string refundId = payment.RefundId ?? $"REF-{payment.CashfreeOrderId}";

            try
            {
                System.Text.Json.JsonDocument? refundResponse = null;
                try
                {
                    refundResponse = await _cashfreeService.InitiateRefundAsync(
                        payment.CashfreeOrderId,
                        refundAmount,
                        refundId,
                        refundReason);
                }
                catch (Exception initEx)
                {
                    _logger.LogWarning(initEx, "InitiateRefund on Cashfree returned error, attempting to check live refund status for order {OrderId}", payment.CashfreeOrderId);
                    try
                    {
                        refundResponse = await _cashfreeService.GetRefundStatusAsync(payment.CashfreeOrderId, refundId);
                    }
                    catch
                    {
                        throw initEx;
                    }
                }

                string cashfreeRefundStatus = "PENDING";
                string? statusDescription = null;
                if (refundResponse.RootElement.TryGetProperty("refund_status", out var stEl))
                {
                    cashfreeRefundStatus = stEl.GetString() ?? "PENDING";
                }
                if (refundResponse.RootElement.TryGetProperty("status_description", out var descEl))
                {
                    statusDescription = descEl.GetString();
                }

                payment.RefundId = refundId;
                payment.RefundReason = refundReason;
                payment.UpdatedAt = DateTime.UtcNow;

                if (cashfreeRefundStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RefundStatus = "Refunded";
                    payment.Status = "REFUNDED";
                    payment.LastError = null;
                }
                else if (cashfreeRefundStatus.Equals("ONHOLD", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RefundStatus = "RefundOnHold";
                    payment.LastError = statusDescription ?? "Refund on hold because of insufficient account balance";
                }
                else if (cashfreeRefundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || cashfreeRefundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RefundStatus = "RefundFailed";
                    payment.LastError = statusDescription ?? "Cashfree refund failed/cancelled.";
                }
                else
                {
                    payment.RefundStatus = "RefundProcessing";
                    payment.LastError = statusDescription;
                }

                // Sync BookingCancellation if present
                var cancelRecord = await _dbContext.BookingCancellations.FirstOrDefaultAsync(c => c.PaymentId == payment.Id);
                if (cancelRecord != null)
                {
                    cancelRecord.CashfreeRefundId = refundId;
                    if (string.Equals(payment.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase))
                    {
                        cancelRecord.RefundStatus = "COMPLETED";
                        cancelRecord.Status = "Completed";
                        cancelRecord.CompletedAtUtc = DateTime.UtcNow;
                    }
                    else if (string.Equals(payment.RefundStatus, "RefundOnHold", StringComparison.OrdinalIgnoreCase))
                    {
                        cancelRecord.RefundStatus = "ON_HOLD";
                        cancelRecord.FailureReason = payment.LastError;
                    }
                    else if (string.Equals(payment.RefundStatus, "RefundFailed", StringComparison.OrdinalIgnoreCase))
                    {
                        cancelRecord.RefundStatus = "FAILED";
                        cancelRecord.FailureReason = payment.LastError;
                    }
                    else
                    {
                        cancelRecord.RefundStatus = "PROCESSING";
                    }
                }

                await _dbContext.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = payment.RefundStatus == "RefundOnHold"
                        ? "Refund initiated but placed ONHOLD by Cashfree due to insufficient merchant account balance. Please recharge your Cashfree account."
                        : "Refund processed with Cashfree.",
                    data = new
                    {
                        paymentId = payment.Id,
                        refundId = payment.RefundId,
                        refundStatus = payment.RefundStatus,
                        refundReason = payment.RefundReason,
                        statusDescription = payment.LastError,
                        gatewayStatus = cashfreeRefundStatus
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin failed to dispatch refund for Payment {PaymentId}", id);
                payment.LastError = ex.Message;
                payment.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to dispatch refund to Cashfree: " + ex.Message
                });
            }
        }
    }

    public class AdminRefundRequestDto
    {
        public decimal? RefundAmount { get; set; }
        public string? RefundReason { get; set; }
    }
}


