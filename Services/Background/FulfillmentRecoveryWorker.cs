using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Services.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
                .Where(p => p.RefundStatus == "RefundPending" || p.RefundStatus == "RefundFailed")
                .ToListAsync(stoppingToken);

            foreach (var payment in failedRefunds)
            {
                if (payment.RefundAttempts >= 5) continue; // Max retries reached

                try
                {
                    string refundId = payment.RefundId ?? $"REF-{payment.CashfreeOrderId}";
                    await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, payment.FinalPayableAmount, refundId, payment.RefundReason ?? "Retry failed refund");

                    payment.RefundStatus = "Refunded";
                    payment.RefundId = refundId;
                    payment.Status = "REFUNDED";
                    await dbContext.SaveChangesAsync(stoppingToken);
                    
                    _logger.LogInformation("Successfully recovered refund for Payment {PaymentId}", payment.Id);
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

                    var request = new PickNBook.Api.Models.DTOs.GetCancelStatusRequestDto
                    {
                        EndUserIp = "127.0.0.1",
                        ChangeRequestId = cancelRecord.SrdvChangeRequestId!
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

                    string cStatus = "Completed";
                    if (resp.TryGetProperty("CancelStatus", out var csNode))
                        cStatus = csNode.ToString() ?? "Completed";
                    
                    cancelReq.CancellationStatus = cStatus;
                    cancelReq.CustomerRefundStatus = cStatus;
                    cancelReq.AdminRefundStatus = cStatus;
                    cancelRecord.SrdvStatus = cStatus;

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

                        // PERSIST FIRST
                        await dbContext.SaveChangesAsync(stoppingToken);

                        // THEN Initiate Cashfree Refund
                        if (calculatedRefund.FinalCustomerRefundAmount > 0)
                        {
                            var payment = await dbContext.Payments.FindAsync(new object[] { cancelRecord.PaymentId }, stoppingToken);
                            if (payment != null && payment.CashfreeOrderId != null)
                            {
                                try
                                {
                                    await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, calculatedRefund.FinalCustomerRefundAmount, cancelRecord.CashfreeRefundId, "Flight Cancellation via Background Poller");
                                    cancelRecord.Status = "RefundInitiated";
                                }
                                catch (Exception ex)
                                {
                                    cancelRecord.Status = "RefundFailed";
                                    cancelRecord.FailureReason = ex.Message;
                                    _logger.LogError(ex, "Failed to initiate Cashfree refund for BookingCancellation {CancellationId}", cancelRecord.Id);
                                }
                            }
                            else
                            {
                                cancelRecord.Status = "Failed";
                                cancelRecord.FailureReason = "Payment or CashfreeOrderId missing.";
                            }
                        }
                        else
                        {
                            cancelRecord.Status = "Completed";
                            cancelRecord.CompletedAtUtc = DateTime.UtcNow;
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);

                        if (cancelRecord.Status == "RefundInitiated" || cancelRecord.Status == "Completed")
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
    }
}
