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

            var pendingRequests = await dbContext.FlightCancellationRequests
                .Where(c => c.CancellationStatus == "Pending" && c.SrdvChangeRequestId != null)
                .ToListAsync(stoppingToken);

            foreach (var cancelReq in pendingRequests)
            {
                try
                {
                    var request = new PickNBook.Api.Models.DTOs.GetCancelStatusRequestDto
                    {
                        EndUserIp = "127.0.0.1",
                        ChangeRequestId = cancelReq.SrdvChangeRequestId!
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
                    
                    if (!isSuccess) continue; // Still pending or failed at SRDV, wait for next poll

                    string cStatus = "Completed";
                    if (resp.TryGetProperty("CancelStatus", out var csNode))
                        cStatus = csNode.ToString() ?? "Completed";
                    
                    cancelReq.CancellationStatus = cStatus;
                    cancelReq.CustomerRefundStatus = cStatus;
                    cancelReq.AdminRefundStatus = cStatus;

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
                            OriginalCustomerPaid = res.CustomerFareInr,
                            SupplierAmount = res.NetFareInr,
                            MarkupAmount = res.MarkupAmount,
                            DiscountAmount = res.CouponDiscount,
                            ConvenienceFee = 0m,
                            SupplierCancellationCharge = cancellationCharge,
                            SupplierRefundAmount = refundAmount
                        };

                        var calculatedRefund = refundCalculator.CalculateCustomerRefund(
                            refundInput,
                            refundMarkup: false,
                            refundConvenienceFee: false,
                            refundCoupon: false);
                        
                        cancelReq.CustomerRefundAmountInr = calculatedRefund.FinalCustomerRefundAmount;
                        cancelReq.AdminRefundAmountInr = refundAmount;
                        cancelReq.CustomerCancellationChargeInr = calculatedRefund.SupplierCancellationCharge + calculatedRefund.MarkupRetained;
                        cancelReq.AdminCancellationChargeInr = cancellationCharge;
                        
                        res.Status = cancelReq.IsPartialCancellation ? "Partially Cancelled" : "Cancelled";
                        res.CancelledAtUtc = DateTime.UtcNow;
                        res.RefundAmountInr = calculatedRefund.FinalCustomerRefundAmount;
                        res.CancellationChargeInr = cancellationCharge;

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

                        // Initiate Cashfree Refund
                        if (calculatedRefund.FinalCustomerRefundAmount > 0)
                        {
                            var payment = await dbContext.Payments.FirstOrDefaultAsync(p => p.UserId == res.UserId && p.BookingReferenceId == res.Id && p.BookingType == "Flight", stoppingToken);
                            if (payment != null && payment.CashfreeOrderId != null)
                            {
                                string refundId = $"REF-CANCEL-{res.Id}-{cancelReq.Id}";
                                await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, calculatedRefund.FinalCustomerRefundAmount, refundId, "Flight Cancellation via Background Poller");
                            }
                        }
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Successfully polled and processed flight cancellation for ChangeRequestId {ChangeRequestId}", cancelReq.SrdvChangeRequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to poll flight cancellation for ChangeRequestId {ChangeRequestId}", cancelReq.SrdvChangeRequestId);
                }
            }
        }
    }
}
