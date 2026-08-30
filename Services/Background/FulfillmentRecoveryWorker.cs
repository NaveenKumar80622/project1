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
                .Where(p => p.FulfillmentStatus == "Pending")
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

            // Find payments that are stuck InProgress or Failed_LocalPersistence
            var strandedPayments = await dbContext.Payments
                .Where(p => (p.FulfillmentStatus == "InProgress" && p.UpdatedAt < thresholdTime) ||
                             p.FulfillmentStatus == "Failed_LocalPersistence")
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
    }
}
