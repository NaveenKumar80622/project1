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

                HotelReservation? curHotel = null;
                BusReservation? curBus = null;
                FlightReservation? curFlight = null;

                if (string.Equals(p.BookingType, "Hotel", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue && hotelMap.TryGetValue(p.BookingId.Value, out curHotel))
                {
                    bookingRef = curHotel.BookingReference;
                    pnr = !string.IsNullOrWhiteSpace(curHotel.ConfirmationNo) ? curHotel.ConfirmationNo : (!string.IsNullOrWhiteSpace(curHotel.ProviderBookingId) ? curHotel.ProviderBookingId : pnr);
                    if (!string.IsNullOrWhiteSpace(curHotel.Status)) srdvStatus = curHotel.Status;
                    summary = !string.IsNullOrWhiteSpace(curHotel.HotelName) ? $"{curHotel.HotelName} ({curHotel.CheckInDate:dd MMM} - {curHotel.CheckOutDate:dd MMM})" : "Hotel Stay";
                    if (string.Equals(curHotel.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) isCancelled = true;
                    if (cancelCharges == 0 && curHotel.CancellationCharges > 0) cancelCharges = curHotel.CancellationCharges;
                    if (refundAmt == 0 && curHotel.RefundAmount > 0) refundAmt = curHotel.RefundAmount;
                }
                else if (string.Equals(p.BookingType, "Bus", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue && busMap.TryGetValue(p.BookingId.Value, out curBus))
                {
                    bookingRef = curBus.BookingReference;
                    pnr = !string.IsNullOrWhiteSpace(curBus.Pnr) ? curBus.Pnr : pnr;
                    if (!string.IsNullOrWhiteSpace(curBus.Status)) srdvStatus = curBus.Status;
                    summary = curBus.BusBooking != null ? $"{curBus.BusBooking.FromCity} → {curBus.BusBooking.ToCity} ({curBus.BusBooking.OperatorName})" : "Bus Journey";
                    if (string.Equals(curBus.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(curBus.Status, "Partially Cancelled", StringComparison.OrdinalIgnoreCase)) isCancelled = true;
                    if (cancelCharges == 0 && (curBus.CancellationChargeInr ?? 0) > 0) cancelCharges = curBus.CancellationChargeInr!.Value;
                    if (refundAmt == 0 && (curBus.RefundAmountInr ?? 0) > 0) refundAmt = curBus.RefundAmountInr!.Value;
                }
                else if (string.Equals(p.BookingType, "Flight", StringComparison.OrdinalIgnoreCase) && p.BookingId.HasValue && flightMap.TryGetValue(p.BookingId.Value, out curFlight))
                {
                    bookingRef = curFlight.BookingReference;
                    pnr = !string.IsNullOrWhiteSpace(curFlight.Pnr) ? curFlight.Pnr : pnr;
                    if (!string.IsNullOrWhiteSpace(curFlight.Status)) srdvStatus = curFlight.Status;
                    summary = !string.IsNullOrWhiteSpace(curFlight.Airline) ? $"{curFlight.Airline} ({curFlight.FromCity} → {curFlight.ToCity})" : "Flight Journey";
                    if (string.Equals(curFlight.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) isCancelled = true;
                    if (cancelCharges == 0 && (curFlight.CancellationChargeInr ?? 0) > 0) cancelCharges = curFlight.CancellationChargeInr!.Value;
                    if (refundAmt == 0 && (curFlight.RefundAmountInr ?? 0) > 0) refundAmt = curFlight.RefundAmountInr!.Value;
                }

                var hierarchy = BuildLifecycleHierarchy(p, exec, cancel, curHotel, curBus, curFlight, bookingRef, pnr, srdvStatus, isCancelled, cancelCharges, refundAmt);

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
                    SupplierError = exec?.LastError,

                    // Hierarchy & Tracing
                    CanonicalStatus = hierarchy.CanonicalStatus,
                    CanonicalStatusLabel = hierarchy.CanonicalStatusLabel,
                    LifecycleHierarchy = new
                    {
                        hierarchy.CanonicalStatus,
                        hierarchy.CanonicalStatusLabel,
                        hierarchy.CurrentStageIndex,
                        hierarchy.CurrentStageKey,
                        hierarchy.CurrentStageName,
                        hierarchy.TotalStages,
                        hierarchy.IsTerminal,
                        hierarchy.NextActionRequired
                    }
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

            var hierarchy = BuildLifecycleHierarchy(payment, exec, cancel, hotel, bus, flight, bookingRef, pnr, srdvStatus, isCancelled, cancelCharges, refundAmt);

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

                    // Lifecycle Tracing & Hierarchy
                    canonicalStatus = hierarchy.CanonicalStatus,
                    canonicalStatusLabel = hierarchy.CanonicalStatusLabel,
                    lifecycleHierarchy = hierarchy,

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
        private static LifecycleHierarchyDto BuildLifecycleHierarchy(
            Payment p,
            SupplierFulfillmentExecution? exec,
            BookingCancellation? cancel,
            HotelReservation? hotel,
            BusReservation? bus,
            FlightReservation? flight,
            string? bookingRef,
            string? pnr,
            string srdvStatus,
            bool isCancelled,
            decimal cancelCharges,
            decimal refundAmt)
        {
            var stages = new List<LifecycleStageNodeDto>();
            decimal gwPaid = p.GatewayPaidAmount > 0 ? p.GatewayPaidAmount : (p.FinalPayableAmount - p.WalletUsedAmount);

            // 1. Stage: Payment Authorization
            bool isPaymentSuccess = string.Equals(p.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                                    p.PaidAt.HasValue;
            bool isPaymentFailed = string.Equals(p.Status, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(p.Status, "USER_DROPPED", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(p.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);

            string stage1Status = isPaymentSuccess ? "COMPLETED" : (isPaymentFailed ? "FAILED" : "PENDING");
            DateTime? stage1Time = isPaymentSuccess ? (p.PaidAt ?? p.UpdatedAt) : (isPaymentFailed ? p.UpdatedAt : p.CreatedAt);
            string stage1Summary = isPaymentSuccess
                ? $"₹{p.FinalPayableAmount:F2} captured via {p.PaymentMethod ?? "Cashfree"} (Gateway: ₹{gwPaid:F2}, Wallet: ₹{p.WalletUsedAmount:F2})."
                : (isPaymentFailed
                    ? (p.FailureReason ?? p.LastError ?? "Payment dropped or declined by customer bank.")
                    : $"Cashfree Order #{p.CashfreeOrderId} created for ₹{p.FinalPayableAmount:F2}. Awaiting customer payment.");

            stages.Add(new LifecycleStageNodeDto
            {
                StageIndex = 0,
                Key = "GATEWAY_PAYMENT",
                Name = "Payment Authorization",
                Status = stage1Status,
                Timestamp = stage1Time,
                Summary = stage1Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "orderId", p.CashfreeOrderId },
                    { "paymentId", p.CashfreePaymentId },
                    { "amount", p.FinalPayableAmount },
                    { "gatewayPaid", gwPaid },
                    { "walletUsed", p.WalletUsedAmount },
                    { "currency", p.Currency },
                    { "method", p.PaymentMethod ?? "Cashfree" },
                    { "isPaid", isPaymentSuccess }
                }
            });

            // 2. Stage: SRDV Supplier Dispatch
            bool hasDispatched = exec != null || !string.Equals(p.FulfillmentStatus, "Pending", StringComparison.OrdinalIgnoreCase);
            string stage2Status;
            DateTime? stage2Time = null;
            string stage2Summary;

            if (!isPaymentSuccess)
            {
                stage2Status = "SKIPPED";
                stage2Summary = "Awaiting payment authorization before supplier dispatch.";
            }
            else if (hasDispatched)
            {
                stage2Status = "COMPLETED";
                stage2Time = exec?.CreatedAt ?? p.UpdatedAt;
                stage2Summary = $"Dispatched {p.BookingType} booking request to SRDV supplier API (Reservation #{p.BookingId}).";
            }
            else
            {
                stage2Status = "IN_PROGRESS";
                stage2Time = p.PaidAt;
                stage2Summary = "Payment verified. Queued for supplier booking dispatch.";
            }

            stages.Add(new LifecycleStageNodeDto
            {
                StageIndex = 1,
                Key = "SUPPLIER_DISPATCH",
                Name = "Supplier Dispatch",
                Status = stage2Status,
                Timestamp = stage2Time,
                Summary = stage2Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "bookingType", p.BookingType },
                    { "bookingId", p.BookingId },
                    { "executionId", exec?.Id }
                }
            });

            // 3. Stage: Supplier Confirmation & PNR
            bool isFulfillSuccess = string.Equals(p.FulfillmentStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(p.FulfillmentStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(exec?.SupplierBookingStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(exec?.SupplierBookingStatus, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(srdvStatus, "Booked", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(srdvStatus, "Confirmed", StringComparison.OrdinalIgnoreCase);

            bool isFulfillFailed = (p.FulfillmentStatus != null && p.FulfillmentStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) ||
                                   (exec?.SupplierBookingStatus != null && exec.SupplierBookingStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) ||
                                   string.Equals(srdvStatus, "Failed", StringComparison.OrdinalIgnoreCase);

            string stage3Status;
            DateTime? stage3Time = null;
            string stage3Summary;

            if (!isPaymentSuccess)
            {
                stage3Status = "SKIPPED";
                stage3Summary = "Skipped because payment was not completed.";
            }
            else if (isFulfillSuccess)
            {
                stage3Status = "COMPLETED";
                stage3Time = exec?.UpdatedAt ?? p.UpdatedAt;
                stage3Summary = $"SRDV confirmed reservation #{p.BookingId}. Provider Reference / PNR: {pnr ?? exec?.SupplierReference ?? "Confirmed"}.";
            }
            else if (isFulfillFailed)
            {
                stage3Status = "FAILED";
                stage3Time = exec?.UpdatedAt ?? p.UpdatedAt;
                stage3Summary = exec?.LastError ?? p.LastError ?? "Supplier fulfillment failed during confirmation.";
            }
            else
            {
                stage3Status = "IN_PROGRESS";
                stage3Time = exec?.CreatedAt ?? p.UpdatedAt;
                stage3Summary = "SRDV supplier is confirming inventory and issuing PNR/Ticket.";
            }

            stages.Add(new LifecycleStageNodeDto
            {
                StageIndex = 2,
                Key = "SUPPLIER_CONFIRMATION",
                Name = "Supplier Booking & PNR",
                Status = stage3Status,
                Timestamp = stage3Time,
                Summary = stage3Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "pnr", pnr },
                    { "bookingReference", bookingRef },
                    { "supplierStatus", srdvStatus },
                    { "providerReference", exec?.SupplierReference },
                    { "supplierError", exec?.LastError }
                }
            });

            // 4. Stage: Post-Booking Lifecycle
            string stage4Status;
            DateTime? stage4Time = null;
            string stage4Summary;

            if (!isPaymentSuccess || isFulfillFailed)
            {
                stage4Status = "SKIPPED";
                stage4Summary = "Not applicable (booking was not established).";
            }
            else if (!isFulfillSuccess)
            {
                stage4Status = "PENDING";
                stage4Summary = "Awaiting supplier booking confirmation.";
            }
            else if (isCancelled)
            {
                stage4Status = "COMPLETED";
                stage4Time = cancel?.CreatedAtUtc ?? hotel?.CancelledAt ?? bus?.CancelledAtUtc ?? flight?.CancelledAtUtc ?? p.UpdatedAt;
                stage4Summary = $"Booking cancelled. Supplier penalty: ₹{cancelCharges:F2}, Eligible refund: ₹{refundAmt:F2}.";
            }
            else
            {
                stage4Status = "COMPLETED";
                stage4Time = exec?.UpdatedAt ?? p.PaidAt;
                stage4Summary = "Booking is confirmed and active.";
            }

            stages.Add(new LifecycleStageNodeDto
            {
                StageIndex = 3,
                Key = "POST_BOOKING_LIFECYCLE",
                Name = isCancelled ? "Booking Cancellation" : "Active Reservation",
                Status = stage4Status,
                Timestamp = stage4Time,
                Summary = stage4Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "isCancelled", isCancelled },
                    { "cancellationCharges", cancelCharges },
                    { "eligibleRefundAmount", refundAmt },
                    { "cancellationReason", cancel?.FailureReason ?? hotel?.CancellationReason ?? bus?.CancellationReason ?? flight?.CancellationReason ?? p.RefundReason }
                }
            });

            // 5. Stage: Refund Settlement
            bool isRefundDone = string.Equals(p.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(cancel?.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase);

            bool isRefundOnHold = string.Equals(p.RefundStatus, "RefundOnHold", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(cancel?.RefundStatus, "ON_HOLD", StringComparison.OrdinalIgnoreCase);

            bool isRefundProcessing = string.Equals(p.RefundStatus, "RefundProcessing", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(p.RefundStatus, "PROCESSING", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(cancel?.RefundStatus, "PROCESSING", StringComparison.OrdinalIgnoreCase);

            bool isRefundFailed = string.Equals(p.RefundStatus, "RefundFailed", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(cancel?.RefundStatus, "FAILED", StringComparison.OrdinalIgnoreCase);

            string stage5Status;
            DateTime? stage5Time = null;
            string stage5Summary;

            if (!isCancelled && !isFulfillFailed)
            {
                stage5Status = "SKIPPED";
                stage5Summary = "Not applicable (booking active, no refund requested).";
            }
            else if (isRefundDone)
            {
                stage5Status = "COMPLETED";
                stage5Time = cancel?.CompletedAtUtc ?? p.UpdatedAt;
                decimal settledAmt = refundAmt > 0 ? refundAmt : p.FinalPayableAmount;
                stage5Summary = $"Refund of ₹{settledAmt:F2} settled. Cashfree Refund ID: {p.RefundId ?? cancel?.CashfreeRefundId ?? "Settled"}.";
            }
            else if (isRefundOnHold)
            {
                stage5Status = "WARNING";
                stage5Time = p.UpdatedAt;
                stage5Summary = "Refund on hold at Cashfree due to insufficient merchant account balance.";
            }
            else if (isRefundProcessing)
            {
                stage5Status = "IN_PROGRESS";
                stage5Time = p.UpdatedAt;
                stage5Summary = $"Refund of ₹{refundAmt:F2} is processing with Cashfree.";
            }
            else if (isRefundFailed)
            {
                stage5Status = "FAILED";
                stage5Time = p.UpdatedAt;
                stage5Summary = p.LastError ?? "Cashfree refund failed or rejected.";
            }
            else
            {
                stage5Status = "PENDING";
                stage5Time = p.UpdatedAt;
                stage5Summary = $"Eligible refund amount: ₹{refundAmt:F2}. Awaiting admin / auto refund initiation.";
            }

            stages.Add(new LifecycleStageNodeDto
            {
                StageIndex = 4,
                Key = "REFUND_SETTLEMENT",
                Name = "Refund Settlement",
                Status = stage5Status,
                Timestamp = stage5Time,
                Summary = stage5Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "refundId", p.RefundId ?? cancel?.CashfreeRefundId },
                    { "refundStatus", p.RefundStatus ?? cancel?.RefundStatus },
                    { "refundAmount", refundAmt > 0 ? refundAmt : (isRefundDone ? p.FinalPayableAmount : 0m) },
                    { "cashfreeError", p.LastError }
                }
            });

            // Canonical synthesis
            string canonicalStatus;
            string canonicalLabel;
            int currentStageIndex;
            string currentStageKey;
            string currentStageName;
            bool isTerminal;
            string nextAction;

            if (isPaymentFailed)
            {
                canonicalStatus = "PAYMENT_FAILED";
                canonicalLabel = "Payment Failed / Dropped";
                currentStageIndex = 0;
                currentStageKey = "GATEWAY_PAYMENT";
                currentStageName = "Payment Authorization";
                isTerminal = true;
                nextAction = "None (Customer abandoned or bank declined)";
            }
            else if (!isPaymentSuccess)
            {
                canonicalStatus = "PAYMENT_PENDING";
                canonicalLabel = "Payment Pending";
                currentStageIndex = 0;
                currentStageKey = "GATEWAY_PAYMENT";
                currentStageName = "Payment Authorization";
                isTerminal = false;
                nextAction = "Awaiting customer to complete payment";
            }
            else if (isFulfillFailed)
            {
                currentStageIndex = 4;
                currentStageKey = "REFUND_SETTLEMENT";
                currentStageName = "Refund Settlement";

                if (isRefundDone)
                {
                    canonicalStatus = "BOOKING_FAILED_REFUNDED";
                    canonicalLabel = "Booking Failed & Refunded";
                    isTerminal = true;
                    nextAction = "None (Refund fully settled)";
                }
                else if (isRefundOnHold)
                {
                    canonicalStatus = "REFUND_ON_HOLD";
                    canonicalLabel = "Refund On Hold (Low Balance)";
                    isTerminal = false;
                    nextAction = "Recharge Cashfree merchant balance to release refund";
                }
                else if (isRefundProcessing)
                {
                    canonicalStatus = "BOOKING_FAILED_REFUND_PROCESSING";
                    canonicalLabel = "Booking Failed - Refund Processing";
                    isTerminal = false;
                    nextAction = "Monitor Cashfree refund status";
                }
                else
                {
                    canonicalStatus = "BOOKING_FAILED_REFUND_PENDING";
                    canonicalLabel = "Booking Failed - Refund Pending";
                    isTerminal = false;
                    nextAction = "Trigger refund to customer from Admin action button";
                }
            }
            else if (!isFulfillSuccess)
            {
                canonicalStatus = "PAYMENT_SUCCESS_FULFILLING";
                canonicalLabel = "Paid - Fulfilling with SRDV";
                currentStageIndex = 1;
                currentStageKey = "SUPPLIER_DISPATCH";
                currentStageName = "Supplier Dispatch";
                isTerminal = false;
                nextAction = "Awaiting SRDV supplier confirmation";
            }
            else if (isCancelled)
            {
                currentStageIndex = 4;
                currentStageKey = "REFUND_SETTLEMENT";
                currentStageName = "Refund Settlement";

                if (isRefundDone)
                {
                    canonicalStatus = "CANCELLED_AND_REFUNDED";
                    canonicalLabel = "Cancelled & Refunded";
                    isTerminal = true;
                    nextAction = "None (Cancellation & refund complete)";
                }
                else if (isRefundOnHold)
                {
                    canonicalStatus = "REFUND_ON_HOLD";
                    canonicalLabel = "Refund On Hold (Low Balance)";
                    isTerminal = false;
                    nextAction = "Recharge Cashfree merchant balance to release refund";
                }
                else if (isRefundProcessing)
                {
                    canonicalStatus = "CANCELLED_REFUND_PROCESSING";
                    canonicalLabel = "Cancelled - Refund Processing";
                    isTerminal = false;
                    nextAction = "Monitor Cashfree refund status";
                }
                else
                {
                    canonicalStatus = "CANCELLED_REFUND_PENDING";
                    canonicalLabel = "Cancelled - Refund Pending";
                    isTerminal = false;
                    nextAction = "Initiate customer refund via action button";
                }
            }
            else
            {
                canonicalStatus = "BOOKING_CONFIRMED";
                canonicalLabel = "Booking Confirmed";
                currentStageIndex = 3;
                currentStageKey = "POST_BOOKING_LIFECYCLE";
                currentStageName = "Active Reservation";
                isTerminal = true;
                nextAction = "None (Reservation active & confirmed)";
            }

            return new LifecycleHierarchyDto
            {
                CanonicalStatus = canonicalStatus,
                CanonicalStatusLabel = canonicalLabel,
                CurrentStageIndex = currentStageIndex,
                CurrentStageKey = currentStageKey,
                CurrentStageName = currentStageName,
                TotalStages = stages.Count,
                IsTerminal = isTerminal,
                NextActionRequired = nextAction,
                Stages = stages
            };
        }
    }

    public class AdminRefundRequestDto
    {
        public decimal? RefundAmount { get; set; }
        public string? RefundReason { get; set; }
    }

    public class LifecycleHierarchyDto
    {
        public string CanonicalStatus { get; set; } = string.Empty;
        public string CanonicalStatusLabel { get; set; } = string.Empty;
        public int CurrentStageIndex { get; set; }
        public string CurrentStageKey { get; set; } = string.Empty;
        public string CurrentStageName { get; set; } = string.Empty;
        public int TotalStages { get; set; } = 5;
        public bool IsTerminal { get; set; }
        public string NextActionRequired { get; set; } = string.Empty;
        public List<LifecycleStageNodeDto> Stages { get; set; } = new();
    }

    public class LifecycleStageNodeDto
    {
        public int StageIndex { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, object?> Meta { get; set; } = new();
    }
}


