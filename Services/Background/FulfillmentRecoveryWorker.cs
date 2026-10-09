using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Background
{
    public class FulfillmentRecoveryWorker : BackgroundService
    {
        private readonly ILogger<FulfillmentRecoveryWorker> _logger;
        private readonly IServiceProvider _serviceProvider;

        public FulfillmentRecoveryWorker(ILogger<FulfillmentRecoveryWorker> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FulfillmentRecoveryWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingFulfillmentsAsync(stoppingToken);
                    await ProcessFailedRefundsAsync(stoppingToken);
                    await ProcessStrandedFulfillmentsAsync(stoppingToken);
                    await ProcessPendingFlightCancellationsAsync(stoppingToken);
                    await ProcessPendingBusCancellationsAsync(stoppingToken);
                    await ProcessExpiredReservationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing FulfillmentRecoveryWorker.");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private async Task ProcessPendingFulfillmentsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IBookingOrchestratorService>();

            var pendingPayments = await dbContext.Payments
                .Where(p => p.FulfillmentStatus == "Pending" && (p.Status == PickNBook.Api.Models.Payments.PaymentStatus.Success || p.Status == "PAID"))
                .Select(p => p.Id)
                .ToListAsync(stoppingToken);

            foreach (var paymentId in pendingPayments)
            {
                try
                {
                    await orchestrator.ProcessFulfillmentAsync(paymentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background worker failed to process fulfillment for payment {PaymentId}", paymentId);
                }
            }
        }

        private async Task ProcessFailedRefundsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cashfreeService = scope.ServiceProvider.GetRequiredService<ICashfreeService>();

            var failedRefunds = await dbContext.Payments
                .Where(p => p.RefundStatus == "RefundPending" 
                         || p.RefundStatus == "RefundFailed" 
                         || p.RefundStatus == "RefundOnHold" 
                         || p.RefundStatus == "PENDING")
                .ToListAsync(stoppingToken);

            foreach (var payment in failedRefunds)
            {
                if (payment.RefundAttempts >= 5) continue; // Max retries reached

                try
                {
                    string refundId = payment.RefundId ?? $"REF-{payment.CashfreeOrderId}";
                    System.Text.Json.JsonDocument? refundDoc = null;

                    // If refund was already submitted to Cashfree and is on hold or pending, check live gateway status first
                    if (!string.IsNullOrEmpty(payment.RefundId) || payment.RefundStatus == "RefundOnHold")
                    {
                        try
                        {
                            refundDoc = await cashfreeService.GetRefundStatusAsync(payment.CashfreeOrderId, refundId);
                        }
                        catch (Exception qEx)
                        {
                            _logger.LogWarning(qEx, "Failed to query live refund status for Order {OrderId}, refund {RefundId}", payment.CashfreeOrderId, refundId);
                        }
                    }

                    // If not found on gateway or status check wasn't possible, initiate
                    if (refundDoc == null)
                    {
                        refundDoc = await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, payment.FinalPayableAmount, refundId, payment.RefundReason ?? "Retry failed refund");
                    }

                    string cashfreeRefundStatus = "PENDING";
                    string? statusDescription = null;
                    if (refundDoc.RootElement.TryGetProperty("refund_status", out var stEl))
                    {
                        cashfreeRefundStatus = stEl.GetString() ?? "PENDING";
                    }
                    if (refundDoc.RootElement.TryGetProperty("status_description", out var descEl))
                    {
                        statusDescription = descEl.GetString();
                    }

                    payment.RefundId = refundId;
                    payment.UpdatedAt = DateTime.UtcNow;

                    if (cashfreeRefundStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                    {
                        payment.RefundStatus = "Refunded";
                        payment.Status = "REFUNDED";
                        payment.LastError = null;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Successfully verified and recovered refund for Payment {PaymentId}", payment.Id);
                    }
                    else if (cashfreeRefundStatus.Equals("ONHOLD", StringComparison.OrdinalIgnoreCase))
                    {
                        payment.RefundStatus = "RefundOnHold";
                        payment.LastError = statusDescription ?? "Refund on hold because of insufficient account balance";
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogWarning("Payment {PaymentId} refund {RefundId} is still ONHOLD on Cashfree due to balance shortfall.", payment.Id, refundId);
                    }
                    else if (cashfreeRefundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || cashfreeRefundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        payment.RefundStatus = "RefundFailed";
                        payment.RefundAttempts += 1;
                        payment.LastError = statusDescription ?? "Cashfree refund cancelled/failed.";
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    else
                    {
                        payment.RefundStatus = "RefundProcessing";
                        payment.LastError = statusDescription;
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    payment.RefundAttempts += 1;
                    payment.LastError = ex.Message;
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogError(ex, "Retry refund failed for Payment {PaymentId}", payment.Id);
                }
            }
        }

        private async Task ProcessStrandedFulfillmentsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IBookingOrchestratorService>();

            var thresholdTime = DateTime.UtcNow.AddMinutes(-10);

            // Find payments that are stuck InProgress or Failed_LocalPersistence, BUT only if they are paid
            var strandedPayments = await dbContext.Payments
                .Where(p => ((p.FulfillmentStatus == "InProgress" && p.UpdatedAt < thresholdTime) ||
                             p.FulfillmentStatus == "Failed_LocalPersistence") &&
                            (p.Status == PickNBook.Api.Models.Payments.PaymentStatus.Success || p.Status == "PAID"))
                .ToListAsync(stoppingToken);

            foreach (var payment in strandedPayments)
            {
                _logger.LogWarning("Payment {PaymentId} is stranded in Fulfillment {Status} state. Attempting atomic recovery.", payment.Id, payment.FulfillmentStatus);

                // Atomically claim the payment for recovery to prevent concurrent worker executions
                int claimed = await dbContext.Payments
                    .Where(p => p.Id == payment.Id && p.FulfillmentStatus == payment.FulfillmentStatus)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.FulfillmentStatus, "Recovering")
                        .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), stoppingToken);

                if (claimed > 0)
                {
                    try
                    {
                        await orchestrator.RecoverFulfillmentAsync(payment.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Recovery failed for Payment {PaymentId}", payment.Id);
                        // We do not revert to InProgress here. RecoverFulfillmentAsync should handle terminal states.
                        // If it threw an unhandled exception, it remains in Recovering and can be manually inspected.
                    }
                }
            }
        }

        private async Task ProcessPendingFlightCancellationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var srdvFlightService = scope.ServiceProvider.GetRequiredService<ISrdvFlightService>();
            var cashfreeService = scope.ServiceProvider.GetRequiredService<ICashfreeService>();
            var refundCalculator = scope.ServiceProvider.GetRequiredService<ICancellationRefundCalculator>();
            var emailService = scope.ServiceProvider.GetRequiredService<PickNBook.Api.Services.ITicketEmailService>();
            var refundRouter = scope.ServiceProvider.GetRequiredService<IRefundRouterService>();

            var pendingCancellations = await dbContext.BookingCancellations
                .Where(c => c.Status == "Pending" && c.BookingType == "Flight" && c.SrdvChangeRequestId != null)
                .Select(c => c.Id)
                .ToListAsync(stoppingToken);

            foreach (var cancelId in pendingCancellations)
            {
                try
                {
                    // Atomic Transition: Pending -> Processing
                    var rowsAffected = await dbContext.BookingCancellations
                        .Where(c => c.Id == cancelId && c.Status == "Pending")
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Processing"), stoppingToken);

                    if (rowsAffected == 0) continue; // Another worker claimed it

                    var cancelRecord = await dbContext.BookingCancellations.FindAsync(new object[] { cancelId }, stoppingToken);
                    if (cancelRecord == null) continue;

                    var cancelReq = await dbContext.FlightCancellationRequests
                        .FirstOrDefaultAsync(c => c.SrdvChangeRequestId == cancelRecord.SrdvChangeRequestId, stoppingToken);

                    if (cancelReq == null)
                    {
                        cancelRecord.Status = "Failed";
                        cancelRecord.FailureReason = "FlightCancellationRequest not found.";
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    if (!long.TryParse(cancelRecord.SrdvChangeRequestId, out var crId) || crId <= 0)
                    {
                        cancelRecord.Status = "Failed";
                        cancelRecord.FailureReason = "Invalid SrdvChangeRequestId.";
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    var request = new PickNBook.Api.Models.DTOs.GetCancelStatusRequestDto
                    {
                        ChangeRequestId = crId
                    };

                    var responseRaw = await srdvFlightService.GetCancelStatusRawAsync(request);
                    using var doc = System.Text.Json.JsonDocument.Parse(responseRaw);
                    var root = doc.RootElement;
                    
                    var isSuccess = false;
                    System.Text.Json.JsonElement resp = root;
                    if (root.TryGetProperty("Response", out var responseNode)) resp = responseNode;
                    else if (root.TryGetProperty("Results", out var resultsNode)) resp = resultsNode;
                    
                    if (resp.TryGetProperty("ResponseStatus", out var status))
                    {
                        if (status.ValueKind == System.Text.Json.JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                        if (status.ValueKind == System.Text.Json.JsonValueKind.String && status.ToString() == "1") isSuccess = true;
                    }
                    
                    if (!isSuccess)
                    {
                        // Rollback to Pending for next poll
                        cancelRecord.Status = "Pending";
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    string cancellationStatus = "PENDING";
                    if (resp.TryGetProperty("CancellationStatus", out var csNode))
                    {
                        cancellationStatus = csNode.GetString() ?? "PENDING";
                    }
                    else if (resp.TryGetProperty("CancelStatus", out var legacyCsNode))
                    {
                        cancellationStatus = legacyCsNode.ToString() ?? "PENDING";
                    }

                    int changeRequestStatusCode = 1;
                    if (resp.TryGetProperty("ChangeRequestStatus", out var crsNode) && crsNode.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        changeRequestStatusCode = crsNode.GetInt32();
                    }
                    else if (resp.TryGetProperty("Status", out var sNode) && sNode.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        changeRequestStatusCode = sNode.GetInt32();
                    }

                    bool isSettled = false;
                    if (resp.TryGetProperty("IsSettled", out var isSettledNode))
                    {
                        if (isSettledNode.ValueKind == System.Text.Json.JsonValueKind.True) isSettled = true;
                        else if (isSettledNode.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(isSettledNode.GetString(), out var parsedSettled)) isSettled = parsedSettled;
                    }

                    // If not settled yet (e.g. PENDING or IN_PROCESS), record current progress and keep polling
                    if (!isSettled && (string.Equals(cancellationStatus, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(cancellationStatus, "IN_PROCESS", StringComparison.OrdinalIgnoreCase) ||
                                       changeRequestStatusCode == 1))
                    {
                        cancelReq.CancellationStatus = cancellationStatus;
                        cancelRecord.SrdvStatus = cancellationStatus;
                        cancelRecord.Status = "Pending"; // Re-queue for next polling cycle
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    // If supplier explicitly rejected or failed
                    if (string.Equals(cancellationStatus, "FAILED", StringComparison.OrdinalIgnoreCase) || changeRequestStatusCode == 4)
                    {
                        cancelReq.CancellationStatus = "Rejected";
                        cancelReq.CustomerRefundStatus = "Rejected";
                        cancelReq.AdminRefundStatus = "Rejected";
                        cancelRecord.SrdvStatus = "Rejected";
                        cancelRecord.Status = "Rejected";
                        cancelRecord.FailureReason = "Cancellation was rejected or failed at supplier.";
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    // If MANUAL_CHECK_REQUIRED
                    if (string.Equals(cancellationStatus, "MANUAL_CHECK_REQUIRED", StringComparison.OrdinalIgnoreCase))
                    {
                        cancelReq.CancellationStatus = "MANUAL_CHECK_REQUIRED";
                        cancelRecord.SrdvStatus = "MANUAL_CHECK_REQUIRED";
                        cancelRecord.Status = "MANUAL_CHECK_REQUIRED";
                        cancelRecord.FailureReason = "Supplier reported MANUAL_CHECK_REQUIRED. Operational inspection required.";
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    // Otherwise, if settled and processed/cancelled
                    cancelReq.CancellationStatus = cancellationStatus;
                    cancelReq.CustomerRefundStatus = "Processed";
                    cancelReq.AdminRefundStatus = "Processed";
                    cancelRecord.SrdvStatus = cancellationStatus;

                    decimal refundAmount = 0;
                    if (resp.TryGetProperty("RefundAmount", out var rAmt))
                    {
                        if (rAmt.ValueKind == System.Text.Json.JsonValueKind.Number) refundAmount = rAmt.GetDecimal();
                        else if (rAmt.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(rAmt.ToString(), out var rAmtDec)) refundAmount = rAmtDec;
                    }

                    decimal cancellationCharge = 0;
                    if (resp.TryGetProperty("CancellationCharge", out var cCharge))
                    {
                        if (cCharge.ValueKind == System.Text.Json.JsonValueKind.Number) cancellationCharge = cCharge.GetDecimal();
                        else if (cCharge.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(cCharge.ToString(), out var cChargeDec)) cancellationCharge = cChargeDec;
                    }

                    var res = await dbContext.FlightReservations.Include(x => x.Segments).FirstOrDefaultAsync(x => x.Id == cancelReq.FlightReservationId, stoppingToken);
                    if (res != null) 
                    {
                        var refundInput = new PickNBook.Api.Models.DTOs.RefundCalculationInput
                        {
                            OriginalCustomerPaid = cancelRecord.OriginalCustomerPaid,
                            SupplierAmount = cancelRecord.SupplierAmount,
                            MarkupAmount = cancelRecord.MarkupAmount,
                            DiscountAmount = cancelRecord.DiscountAmount,
                            ConvenienceFee = cancelRecord.ConvenienceFee,
                            SupplierCancellationCharge = cancellationCharge,
                            SupplierRefundAmount = refundAmount
                        };

                        var calculatedRefund = refundCalculator.CalculateCustomerRefund(
                            refundInput);
                        
                        // Populate BookingCancellation LEDGER
                        cancelRecord.SupplierCancellationCharge = calculatedRefund.SupplierCancellationCharge;
                        cancelRecord.SupplierRefundAmount = calculatedRefund.SupplierRefundAmount;
                        cancelRecord.MarkupRefunded = calculatedRefund.MarkupRefunded;
                        cancelRecord.CouponForfeited = calculatedRefund.CouponForfeited;
                        cancelRecord.FeeRefunded = calculatedRefund.FeeRefunded;
                        cancelRecord.CustomerRefundAmount = calculatedRefund.FinalCustomerRefundAmount;
                        cancelRecord.CashfreeRefundId = $"REF-CANCEL-{res.Id}-{cancelRecord.Id}";

                        // Update FlightCancellationRequest
                        cancelReq.CustomerRefundAmountInr = calculatedRefund.FinalCustomerRefundAmount;
                        cancelReq.AdminRefundAmountInr = refundAmount;
                        cancelReq.CustomerCancellationChargeInr = calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                        cancelReq.AdminCancellationChargeInr = cancellationCharge;
                        
                        res.Status = cancelReq.IsPartialCancellation ? "Partially Cancelled" : "Cancelled";
                        res.CancelledAtUtc = DateTime.UtcNow;

                        // DO NOT Overwrite financial snapshot on FlightReservation (NetFareInr, TotalPriceInr, etc).
                        // Just update status properties.
                        
                        var passengers = await dbContext.FlightReservationPassengers.Where(p => p.FlightReservationId == res.Id).ToListAsync(stoppingToken);

                        if (cancelReq.IsPartialCancellation)
                        {
                            if (!string.IsNullOrEmpty(cancelReq.CancelledSectorsJson))
                            {
                                var sectors = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PickNBook.Api.Models.DTOs.ChangeRequestSectorDto>>(cancelReq.CancelledSectorsJson);
                                if (sectors != null)
                                {
                                    foreach (var sec in sectors)
                                    {
                                        var matchedSeg = res.Segments.FirstOrDefault(s => string.Equals(s.FromCity, sec.Origin, StringComparison.OrdinalIgnoreCase) && string.Equals(s.ToCity, sec.Destination, StringComparison.OrdinalIgnoreCase));
                                        if (matchedSeg != null) matchedSeg.Status = "Cancelled";
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(cancelReq.CancelledPassengersJson))
                            {
                                var paxs = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PickNBook.Api.Models.DTOs.ChangeRequestTicketDataDto>>(cancelReq.CancelledPassengersJson);
                                if (paxs != null)
                                {
                                    foreach (var px in paxs)
                                    {
                                        var matchedPx = passengers.FirstOrDefault(p => string.Equals(p.FirstName, px.FirstName, StringComparison.OrdinalIgnoreCase) && string.Equals(p.LastName, px.LastName, StringComparison.OrdinalIgnoreCase));
                                        if (matchedPx != null) matchedPx.Status = "Cancelled";
                                    }
                                }
                            }
                        }
                        else
                        {
                            foreach (var seg in res.Segments) seg.Status = "Cancelled";
                            foreach (var pax in passengers) pax.Status = "Cancelled";
                        }

                        // Release flight coupon usage if full cancellation
                        if (!cancelReq.IsPartialCancellation)
                        {
                            var couponUsage = await dbContext.FlightCouponUsages
                                .FirstOrDefaultAsync(u => u.FlightReservationId == res.Id && u.BookingStatus == "Booked", stoppingToken);
                            if (couponUsage != null)
                            {
                                couponUsage.BookingStatus = "Cancelled";
                                var normCode = couponUsage.CouponCode.Trim().ToUpperInvariant();
                                await dbContext.FlightCoupons
                                    .Where(x => x.CouponCode == normCode)
                                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount > 0 ? p.UsedCount - 1 : 0), stoppingToken);
                            }
                        }

                        // PERSIST FIRST
                        await dbContext.SaveChangesAsync(stoppingToken);

                        // THEN Route Refund via RefundRouter
                        if (calculatedRefund.FinalCustomerRefundAmount > 0)
                        {
                            var payment = await dbContext.Payments.FindAsync(new object[] { cancelRecord.PaymentId }, stoppingToken);
                            int.TryParse(cancelRecord.UserId, out int uId);

                            var routeRes = await refundRouter.RouteAsync(new PickNBook.Api.Services.Interfaces.RefundRouteContext
                            {
                                UserId = uId,
                                BookingType = "Flight",
                                BookingReference = res.BookingReference,
                                RefundAmount = calculatedRefund.FinalCustomerRefundAmount,
                                PaymentMethod = payment?.PaymentMethod ?? res.PaymentMethod ?? "Cashfree",
                                CashfreeOrderId = payment?.CashfreeOrderId,
                                RefundPreference = cancelRecord.RefundPreference ?? cancelReq.RefundPreference,
                                Reason = "Flight Cancellation via Background Poller",
                                TotalPaidAmount = payment?.TotalAmount ?? payment?.FinalPayableAmount ?? res.TotalPriceInr,
                                WalletPaidAmount = payment?.WalletUsedAmount ?? res.WalletPaidAmount,
                                GatewayPaidAmount = payment?.GatewayPaidAmount ?? res.GatewayPaidAmount,
                                CancellationId = cancelRecord.Id
                            });

                            cancelRecord.WalletRefundAmount = routeRes.WalletRefunded;
                            cancelRecord.GatewayRefundAmount = routeRes.GatewayRefunded;
                            cancelRecord.CashfreeRefundId = routeRes.CashfreeRefundId;
                            cancelRecord.RefundStatus = routeRes.RefundStatus;
                            cancelRecord.Status = routeRes.RefundStatus == "COMPLETED" ? "Completed" : (routeRes.RefundStatus == "Failed" ? "Failed" : "Processing");
                        }
                        else
                        {
                            cancelRecord.Status = "Completed";
                            cancelRecord.RefundStatus = "NOT_REQUIRED";
                            cancelRecord.CompletedAtUtc = DateTime.UtcNow;
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);

                        if (cancelRecord.Status == "RefundInitiated" || cancelRecord.Status == "Completed" || cancelRecord.Status == "Processing")
                        {
                            // Send Email Notification
                            try
                            {
                                var emailReq = new PickNBook.Api.Models.DTOs.SendFlightTicketEmailRequest
                                {
                                    ToEmail = res.PassengerEmail,
                                    PassengerName = res.PassengerName,
                                    BookingReference = res.BookingReference,
                                    Airline = res.Airline,
                                    Origin = res.FromCity,
                                    Destination = res.ToCity,
                                    DepartureTime = res.DepartureTime,
                                    ArrivalTime = res.ArrivalTime,
                                    Pnr = res.Pnr,
                                    Price = cancelRecord.OriginalCustomerPaid,
                                    Currency = "INR",
                                    NonRefundable = res.NonRefundable,
                                    CancellationCharges = res.CancellationCharges,
                                    IsPartialCancellation = cancelReq.IsPartialCancellation,
                                    Passengers = passengers.Select(p => new PickNBook.Api.Models.DTOs.FlightPassengerTicketDto { FullName = p.FullName, Status = p.Status, SeatNumber = p.SeatNumber }).ToList(),
                                    Segments = res.Segments.Select(s => new PickNBook.Api.Models.DTOs.FlightTicketSegmentDto { Airline = s.Airline, FlightNumber = s.FlightNumber, FromCity = s.FromCity, ToCity = s.ToCity, Status = s.Status }).ToList(),
                                    CancelledPassengers = passengers.Where(p => p.Status == "Cancelled").Select(p => new PickNBook.Api.Models.DTOs.FlightPassengerTicketDto { FullName = p.FullName, SeatNumber = p.SeatNumber, Status = p.Status }).ToList(),
                                    CancelledSegments = res.Segments.Where(s => s.Status == "Cancelled").Select(s => new PickNBook.Api.Models.DTOs.FlightTicketSegmentDto { Airline = s.Airline, FlightNumber = s.FlightNumber, FromCity = s.FromCity, ToCity = s.ToCity, Status = s.Status }).ToList()
                                };
                                await emailService.SendFlightCancellationAsync(emailReq, cancelRecord.CustomerRefundAmount);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send cancellation email for Flight Booking {BookingReference}", res.BookingReference);
                            }

                            // Send SMS Notification
                            try
                            {
                                var notificationService = scope.ServiceProvider.GetService<PickNBook.Api.Services.Notifications.Interfaces.INotificationService>();
                                if (notificationService != null && !string.IsNullOrWhiteSpace(res.PassengerPhone))
                                {
                                    await notificationService.EnqueueAsync(
                                        eventType: "FlightBookingCancelled",
                                        channel: "SMS",
                                        recipient: res.PassengerPhone,
                                        templateKey: "FLIGHT_BOOKING_CANCELLED",
                                        payload: new { Reference = res.BookingReference, Status = "Cancelled" }
                                    );
                                }
                            }
                            catch (Exception smsEx)
                            {
                                _logger.LogWarning(smsEx, "Failed to enqueue cancellation SMS for Flight Booking {BookingReference}", res.BookingReference);
                            }

                            // Additive In-App Notifications (Step 4: Flight Cancellation Completed)
                            try
                            {
                                var inAppNotificationService = scope.ServiceProvider.GetService<PickNBook.Api.Services.Interfaces.IInAppNotificationService>();
                                if (inAppNotificationService != null)
                                {
                                    await inAppNotificationService.CreateNotificationAsync(
                                        type: "Cancellation",
                                        category: "Customer",
                                        title: "Flight Booking Cancelled",
                                        message: $"Your flight booking ({res.BookingReference}) has been cancelled. Refund amount: ₹{cancelRecord.CustomerRefundAmount:N2}.",
                                        severity: "Info",
                                        referenceType: "FlightReservation",
                                        referenceId: res.BookingReference,
                                        actionUrl: $"/bookings/{res.BookingReference}",
                                        idempotencyKey: $"CANCEL_FLIGHT_{res.BookingReference}_SUCCESS",
                                        targetUserId: res.UserId
                                    );
                                }
                            }
                            catch (Exception inAppEx)
                            {
                                _logger.LogWarning(inAppEx, "Failed to create in-app notification for flight cancellation {BookingReference}. Non-fatal.", res.BookingReference);
                            }
                        }
                    }

                    _logger.LogInformation("Successfully polled and processed flight cancellation for ChangeRequestId {ChangeRequestId}", cancelReq.SrdvChangeRequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to poll flight cancellation for Id {Id}", cancelId);
                }
            }
        }

        private async Task ProcessPendingBusCancellationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var srdvBusService = scope.ServiceProvider.GetRequiredService<ISrdvBusService>();
            var refundCalculator = scope.ServiceProvider.GetRequiredService<ICancellationRefundCalculator>();
            var refundRouter = scope.ServiceProvider.GetRequiredService<IRefundRouterService>();
            var emailService = scope.ServiceProvider.GetService<ITicketEmailService>();
            var inAppNotificationService = scope.ServiceProvider.GetService<IInAppNotificationService>();

            // Option A: Poll pending bus cancellations waiting for supplier refund settlement
            var pendingRecords = await dbContext.BookingCancellations
                .Where(c => c.BookingType == "Bus" &&
                            (c.RefundStatus == "AWAITING_SUPPLIER" || c.Status == "AwaitingSupplierRefund" || c.SrdvStatus == "MANUAL_PENDING") &&
                            c.TraceId.HasValue && c.TraceId.Value > 0)
                .ToListAsync(stoppingToken);

            if (!pendingRecords.Any()) return;

            foreach (var cancelRecord in pendingRecords)
            {
                try
                {
                    var traceIdStr = cancelRecord.TraceId!.Value.ToString();
                    var details = await srdvBusService.GetBookingDetailsAsync(traceIdStr);
                    if (details == null || !details.Success || details.Result == null)
                    {
                        continue;
                    }

                    // Reconcile matching cancellation record in SRDV booking details
                    SrdvBusBookingDetailsCancellationDto? matchingCancellation = null;
                    if (cancelRecord.ProviderCancelId.HasValue && cancelRecord.ProviderCancelId.Value > 0 && details.Result.Cancellations != null)
                    {
                        matchingCancellation = details.Result.Cancellations
                            .FirstOrDefault(c => c.CancelId == cancelRecord.ProviderCancelId.Value);
                    }

                    if (matchingCancellation == null && details.Result.Cancellations != null && !string.IsNullOrWhiteSpace(cancelRecord.SeatNamesJson))
                    {
                        try
                        {
                            var seats = JsonSerializer.Deserialize<List<string>>(cancelRecord.SeatNamesJson);
                            if (seats != null && seats.Any())
                            {
                                var seatsSet = new HashSet<string>(seats.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
                                matchingCancellation = details.Result.Cancellations
                                    .OrderByDescending(c => c.CompletedAt ?? DateTime.MinValue)
                                    .FirstOrDefault(c =>
                                    {
                                        if (c.SeatName == null || c.SeatName.Count == 0) return false;
                                        var cSeats = new HashSet<string>(c.SeatName.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
                                        return cSeats.SetEquals(seatsSet);
                                    });
                            }
                        }
                        catch { }
                    }

                    string srdvRefundStatus = matchingCancellation?.RefundStatus?.ToUpperInvariant() ?? "MANUAL_PENDING";
                    string srdvStatus = matchingCancellation?.Status?.ToUpperInvariant() ?? "PENDING";

                    // Supplier explicitly rejected or failed the cancellation
                    if (string.Equals(srdvRefundStatus, "REJECTED", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(srdvStatus, "REJECTED", StringComparison.OrdinalIgnoreCase))
                    {
                        cancelRecord.Status = "Rejected";
                        cancelRecord.RefundStatus = "REJECTED";
                        cancelRecord.SrdvStatus = "REJECTED";
                        cancelRecord.FailureReason = "Cancellation rejected by supplier back office.";

                        var resReject = await dbContext.BusReservations
                            .FirstOrDefaultAsync(x => x.BookingReference == cancelRecord.BookingReference, stoppingToken);
                        if (resReject != null)
                        {
                            resReject.FinancialStatus = "SUPPLIER_REJECTED";
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    // Not settled to wallet yet (e.g. MANUAL_PENDING, PENDING, IN_PROCESS) -> keep waiting
                    if (!string.Equals(srdvRefundStatus, "REFUNDED", StringComparison.OrdinalIgnoreCase))
                    {
                        cancelRecord.SrdvStatus = srdvRefundStatus;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    // Supplier has credited wallet (REFUNDED)! Safe to calculate and release customer payout
                    decimal supplierRefundAmount = matchingCancellation?.RefundAmount ?? 0m;
                    decimal supplierCancellationCharge = matchingCancellation?.CancellationCharge ?? 0m;

                    var res = await dbContext.BusReservations
                        .Include(x => x.BusBooking)
                        .FirstOrDefaultAsync(x => x.BookingReference == cancelRecord.BookingReference, stoppingToken);

                    if (res == null || res.BusBooking == null)
                    {
                        continue;
                    }

                    var passengers = await dbContext.BusReservationPassengers
                        .Where(p => p.BusReservationId == res.Id)
                        .ToListAsync(stoppingToken);

                    // If supplier returned 0 refund amount, fallback to policy calculation
                    if (supplierRefundAmount <= 0m)
                    {
                        var targetPax = passengers;
                        if (!string.IsNullOrWhiteSpace(cancelRecord.SeatNamesJson))
                        {
                            try
                            {
                                var seats = JsonSerializer.Deserialize<List<string>>(cancelRecord.SeatNamesJson);
                                if (seats != null && seats.Any())
                                {
                                    var sSet = new HashSet<string>(seats, StringComparer.OrdinalIgnoreCase);
                                    targetPax = passengers.Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber) && sSet.Contains(p.SeatNumber)).ToList();
                                }
                            }
                            catch { }
                        }

                        var policyFallback = PickNBook.Api.Controllers.BusBookingsController.CalculateSrdvRefund(
                            res.BusBooking,
                            targetPax,
                            cancelRecord.SupplierAmount > 0 ? cancelRecord.SupplierAmount : res.NetFareInr,
                            passengers.Count > 0 ? passengers.Count : 1,
                            res.CancellationPolicyJson);

                        if (policyFallback.RefundAmount > 0m || policyFallback.CancellationCharge > 0m)
                        {
                            supplierRefundAmount = policyFallback.RefundAmount;
                            supplierCancellationCharge = policyFallback.CancellationCharge;
                        }
                        else if (supplierCancellationCharge > 0m && cancelRecord.SupplierAmount > supplierCancellationCharge)
                        {
                            supplierRefundAmount = cancelRecord.SupplierAmount - supplierCancellationCharge;
                        }
                    }

                    var refundInput = new RefundCalculationInput
                    {
                        OriginalCustomerPaid = cancelRecord.OriginalCustomerPaid,
                        SupplierAmount = cancelRecord.SupplierAmount,
                        MarkupAmount = cancelRecord.MarkupAmount,
                        DiscountAmount = cancelRecord.DiscountAmount,
                        ConvenienceFee = cancelRecord.ConvenienceFee,
                        SupplierCancellationCharge = supplierCancellationCharge,
                        SupplierRefundAmount = supplierRefundAmount
                    };

                    var calculatedRefund = refundCalculator.CalculateCustomerRefund(refundInput);

                    // Update BookingCancellation
                    cancelRecord.SupplierCancellationCharge = calculatedRefund.SupplierCancellationCharge;
                    cancelRecord.SupplierRefundAmount = calculatedRefund.SupplierRefundAmount;
                    cancelRecord.MarkupRefunded = calculatedRefund.MarkupRefunded;
                    cancelRecord.CouponForfeited = calculatedRefund.CouponForfeited;
                    cancelRecord.FeeRefunded = calculatedRefund.FeeRefunded;
                    cancelRecord.CustomerRefundAmount = calculatedRefund.FinalCustomerRefundAmount;
                    cancelRecord.SrdvStatus = "REFUNDED";
                    cancelRecord.CompletedAtUtc = DateTime.UtcNow;

                    // Update BusReservation
                    res.CancellationChargeInr = (res.CancellationChargeInr ?? 0m) + calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                    res.RefundAmountInr = (res.RefundAmountInr ?? 0m) + calculatedRefund.FinalCustomerRefundAmount;
                    res.FinancialStatus = calculatedRefund.FinalCustomerRefundAmount > 0 ? "REFUNDED" : "NO_REFUND";

                    await dbContext.SaveChangesAsync(stoppingToken);

                    // Route Refund via RefundRouter
                    if (calculatedRefund.FinalCustomerRefundAmount > 0)
                    {
                        var payment = await dbContext.Payments
                            .FirstOrDefaultAsync(p => p.UserId == res.UserId && p.BookingReferenceId == res.Id && p.BookingType == "Bus", stoppingToken);
                        int.TryParse(res.UserId, out int uId);

                        var routeRes = await refundRouter.RouteAsync(new RefundRouteContext
                        {
                            UserId = uId,
                            BookingType = "Bus",
                            BookingReference = res.BookingReference,
                            RefundAmount = calculatedRefund.FinalCustomerRefundAmount,
                            PaymentMethod = payment?.PaymentMethod ?? res.PaymentMethod ?? "Cashfree",
                            CashfreeOrderId = payment?.CashfreeOrderId,
                            RefundPreference = cancelRecord.RefundPreference ?? "OriginalMethod",
                            Reason = "Bus Cancellation - Supplier Credit Settled",
                            TotalPaidAmount = payment?.TotalAmount ?? payment?.FinalPayableAmount ?? res.TotalPriceInr,
                            WalletPaidAmount = payment?.WalletUsedAmount ?? res.WalletPaidAmount,
                            GatewayPaidAmount = payment?.GatewayPaidAmount ?? res.GatewayPaidAmount,
                            CancellationId = cancelRecord.Id
                        });

                        cancelRecord.WalletRefundAmount = routeRes.WalletRefunded;
                        cancelRecord.GatewayRefundAmount = routeRes.GatewayRefunded;
                        cancelRecord.CashfreeRefundId = routeRes.CashfreeRefundId;
                        cancelRecord.RefundStatus = routeRes.RefundStatus;
                        cancelRecord.Status = routeRes.RefundStatus == "COMPLETED" ? "Completed" : (routeRes.RefundStatus == "Failed" ? "Failed" : "Processing");
                    }
                    else
                    {
                        cancelRecord.Status = "Completed";
                        cancelRecord.RefundStatus = "NOT_REQUIRED";
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);

                    // Send email notification
                    if (emailService != null && !string.IsNullOrWhiteSpace(res.PassengerEmail))
                    {
                        try
                        {
                            var newlyCancelledPax = passengers.Where(p => p.IsCancelled).ToList();
                            await emailService.SendBusCancellationAsync(
                                new SendBusTicketEmailRequest
                                {
                                    ToEmail = res.PassengerEmail,
                                    PassengerName = res.PassengerName,
                                    BookingReference = res.BookingReference,
                                    Pnr = res.Pnr,
                                    OperatorName = res.BusBooking.OperatorName,
                                    BusType = res.BusBooking.BusType,
                                    Origin = srdvBusService.MapCityCodeToName(res.BusBooking.FromCity),
                                    Destination = srdvBusService.MapCityCodeToName(res.BusBooking.ToCity),
                                    DepartureTime = res.BusBooking.DepartureTime,
                                    ArrivalTime = res.BusBooking.ArrivalTime,
                                    BoardingPointTime = res.BoardingPointTime ?? res.BusBooking.DepartureTime,
                                    ArrivalPointTime = res.BusBooking.ArrivalTime,
                                    IsOvernightArrival = res.BusBooking.ArrivalTime.Date > res.BusBooking.DepartureTime.Date,
                                    DurationMinutes = (int)(res.BusBooking.ArrivalTime - res.BusBooking.DepartureTime).TotalMinutes,
                                    BoardingPoint = res.BusBooking.BoardingPoint,
                                    ArrivalPoint = !string.IsNullOrWhiteSpace(res.BusBooking.DroppingPoint) ? res.BusBooking.DroppingPoint : res.BusBooking.ToCity,
                                    Price = res.TotalPriceInr,
                                    BaseFare = res.BaseFareInr,
                                    Currency = "INR",
                                    NetFare = res.NetFareInr,
                                    GstPercent = res.GstPercent,
                                    GstAmount = res.GstAmountInr,
                                    AppliedPromotionCode = res.AppliedPromotionCode,
                                    AppliedPromotionType = res.AppliedPromotionType,
                                    DiscountSource = res.DiscountSource,
                                    DiscountAmount = res.DiscountAmountInr > 0 ? res.DiscountAmountInr : null,
                                    SeatNumber = string.Join(", ", newlyCancelledPax.Select(p => p.SeatNumber).Where(s => !string.IsNullOrWhiteSpace(s))),
                                    AutoDiscountAmount = res.AutoDiscountAmountInr,
                                    CouponDiscountAmount = res.CouponDiscountAmountInr,
                                    Passengers = newlyCancelledPax.Select(p => new BusPassengerSeatDto
                                    {
                                        FullName = p.FullName,
                                        Gender = p.Gender,
                                        SeatNumber = p.SeatNumber ?? string.Empty
                                    }).ToList()
                                },
                                cancelRecord.CustomerRefundAmount
                            );
                        }
                        catch (Exception emailEx)
                        {
                            _logger.LogError(emailEx, "Failed to send bus cancellation settlement email for {BookingReference}", res.BookingReference);
                        }
                    }

                    if (inAppNotificationService != null)
                    {
                        try
                        {
                            await inAppNotificationService.CreateNotificationAsync(
                                type: "Cancellation",
                                category: "Customer",
                                title: "Bus Refund Processed",
                                message: $"Your bus booking ({res.BookingReference}) refund of ₹{cancelRecord.CustomerRefundAmount:N2} has been processed.",
                                severity: "Info",
                                referenceType: "BusBooking",
                                referenceId: res.BookingReference,
                                actionUrl: $"/bookings/{res.BookingReference}",
                                idempotencyKey: $"SETTLE_BUS_{res.BookingReference}_{cancelRecord.Id}",
                                targetUserId: res.UserId
                            );
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reconcile pending bus cancellation for record {RecordId}", cancelRecord.Id);
                }
            }
        }

        private async Task ProcessExpiredReservationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            try
            {
                await paymentService.ProcessExpiredReservationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing expired wallet reservations.");
            }
        }
    }
}
