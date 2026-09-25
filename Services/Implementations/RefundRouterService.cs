using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class ComponentRefundAudit
    {
        public string WalletStatus { get; set; } = "NOT_REQUIRED"; // Pending, Processing, Refunded, Failed, NOT_REQUIRED
        public string GatewayStatus { get; set; } = "NOT_REQUIRED"; // Pending, Processing, Refunded, Failed, NOT_REQUIRED
        public decimal WalletRefundAmount { get; set; }
        public decimal GatewayRefundAmount { get; set; }
        public string? WalletError { get; set; }
        public string? GatewayError { get; set; }
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class RefundRouterService : IRefundRouterService
    {
        private readonly IWalletService _walletService;
        private readonly ICashfreeService _cashfreeService;
        private readonly IHybridRefundSplitStrategy _splitStrategy;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<RefundRouterService> _logger;
        private readonly IInAppNotificationService? _inAppNotificationService;

        public RefundRouterService(
            IWalletService walletService,
            ICashfreeService cashfreeService,
            IHybridRefundSplitStrategy splitStrategy,
            AppDbContext dbContext,
            ILogger<RefundRouterService> logger)
            : this(walletService, cashfreeService, splitStrategy, dbContext, logger, null)
        {
        }

        public RefundRouterService(
            IWalletService walletService,
            ICashfreeService cashfreeService,
            IHybridRefundSplitStrategy splitStrategy,
            AppDbContext dbContext,
            ILogger<RefundRouterService> logger,
            IInAppNotificationService? inAppNotificationService)
        {
            _walletService = walletService;
            _cashfreeService = cashfreeService;
            _splitStrategy = splitStrategy;
            _dbContext = dbContext;
            _logger = logger;
            _inAppNotificationService = inAppNotificationService;
        }

        public async Task<CancellationRefundResultDto> RouteAsync(RefundRouteContext context)
        {
            if (context.RefundAmount <= 0)
            {
                return new CancellationRefundResultDto
                {
                    RefundStatus = "NOT_REQUIRED",
                    RefundDestination = "NONE",
                    WalletRefunded = 0m,
                    GatewayRefunded = 0m,
                    WalletStatus = "NOT_REQUIRED",
                    GatewayStatus = "NOT_REQUIRED",
                    Message = "Refund amount is 0. No refund needed."
                };
            }

            // 1. Locate or track BookingCancellation entity
            BookingCancellation? cancellation = null;
            if (context.CancellationId.HasValue && context.CancellationId.Value > 0)
            {
                cancellation = await _dbContext.BookingCancellations.FirstOrDefaultAsync(c => c.Id == context.CancellationId.Value);
            }
            if (cancellation == null && !string.IsNullOrWhiteSpace(context.BookingReference))
            {
                cancellation = await _dbContext.BookingCancellations
                    .OrderByDescending(c => c.Id)
                    .FirstOrDefaultAsync(c => c.BookingReference == context.BookingReference);
            }

            // 2. Terminal Idempotency Check
            if (cancellation != null && (cancellation.RefundStatus == "COMPLETED" || cancellation.RefundStatus == "Refunded"))
            {
                _logger.LogInformation("Cancellation refund for {BookingRef} is already completed. Returning existing state.", context.BookingReference);
                var existingAudit = ParseComponentAudit(cancellation.FailureReason);
                return new CancellationRefundResultDto
                {
                    RefundStatus = "COMPLETED",
                    RefundDestination = cancellation.RefundPreference,
                    WalletRefunded = cancellation.WalletRefundAmount,
                    GatewayRefunded = cancellation.GatewayRefundAmount,
                    WalletStatus = existingAudit.WalletStatus != "NOT_REQUIRED" ? existingAudit.WalletStatus : (cancellation.WalletRefundAmount > 0 ? "Refunded" : "NOT_REQUIRED"),
                    GatewayStatus = existingAudit.GatewayStatus != "NOT_REQUIRED" ? existingAudit.GatewayStatus : (cancellation.GatewayRefundAmount > 0 ? "Refunded" : "NOT_REQUIRED"),
                    CashfreeRefundId = cancellation.CashfreeRefundId,
                    Message = "Refund has already been completed."
                };
            }

            // 3. Concurrency Protection (Atomic Claim)
            if (cancellation != null)
            {
                if (_dbContext.Database.IsRelational())
                {
                    int rowsClaimed = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE BookingCancellations SET Status = 'Processing' WHERE Id = {cancellation.Id} AND Status IN ('Pending', 'Failed', 'Initiated', 'PartiallyRefunded')"
                    );

                    if (rowsClaimed == 0)
                    {
                        await _dbContext.Entry(cancellation).ReloadAsync();
                        var runningAudit = ParseComponentAudit(cancellation.FailureReason);
                        _logger.LogWarning("Cancellation refund for {BookingRef} could not be claimed (already processing/completed).", context.BookingReference);
                        return new CancellationRefundResultDto
                        {
                            RefundStatus = cancellation.RefundStatus ?? "Processing",
                            RefundDestination = cancellation.RefundPreference,
                            WalletRefunded = cancellation.WalletRefundAmount,
                            GatewayRefunded = cancellation.GatewayRefundAmount,
                            WalletStatus = runningAudit.WalletStatus,
                            GatewayStatus = runningAudit.GatewayStatus,
                            CashfreeRefundId = cancellation.CashfreeRefundId,
                            Message = "Refund execution is in progress or completed by another request."
                        };
                    }
                }
                else
                {
                    // Non-relational (in-memory test provider)
                    if (cancellation.Status == "Processing")
                    {
                        var runningAudit = ParseComponentAudit(cancellation.FailureReason);
                        return new CancellationRefundResultDto
                        {
                            RefundStatus = cancellation.RefundStatus ?? "Processing",
                            RefundDestination = cancellation.RefundPreference,
                            WalletRefunded = cancellation.WalletRefundAmount,
                            GatewayRefunded = cancellation.GatewayRefundAmount,
                            WalletStatus = runningAudit.WalletStatus,
                            GatewayStatus = runningAudit.GatewayStatus,
                            CashfreeRefundId = cancellation.CashfreeRefundId,
                            Message = "Refund execution is in progress or completed by another request."
                        };
                    }
                    cancellation.Status = "Processing";
                }
            }

            // 4. Retrieve Existing Component Audit (Survives Restarts/Retries)
            var audit = ParseComponentAudit(cancellation?.FailureReason);
            bool walletAlreadyRefunded = cancellation != null && (cancellation.WalletRefundAmount > 0 || audit.WalletStatus == "Refunded");
            bool gatewayAlreadyInitiated = cancellation != null && (!string.IsNullOrEmpty(cancellation.CashfreeRefundId) && (audit.GatewayStatus == "Refunded" || audit.GatewayStatus == "Processing" || cancellation.GatewayRefundAmount > 0));

            // 5. Determine Component Target Amounts
            bool isUserPreferWallet = string.Equals(context.RefundPreference, "WALLET", StringComparison.OrdinalIgnoreCase);
            bool isWalletPayment = string.Equals(context.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase);
            bool isCashfreePayment = string.Equals(context.PaymentMethod, "Cashfree", StringComparison.OrdinalIgnoreCase);
            bool isHybridPayment = string.Equals(context.PaymentMethod, "Hybrid", StringComparison.OrdinalIgnoreCase);

            decimal targetWalletAmount = 0m;
            decimal targetGatewayAmount = 0m;

            if (isUserPreferWallet)
            {
                // Customer explicitly chose PickNBook Wallet: 100% of refundable amount to Wallet
                targetWalletAmount = context.RefundAmount;
                targetGatewayAmount = 0m;
            }
            else if (isWalletPayment)
            {
                // Wallet-only booking: 100% of refundable amount to Wallet
                targetWalletAmount = context.RefundAmount;
                targetGatewayAmount = 0m;
            }
            else if (isHybridPayment)
            {
                // Hybrid booking with Original Payment preference: Proportional split
                var split = _splitStrategy.CalculateSplit(
                    context.RefundAmount,
                    context.WalletPaidAmount,
                    context.GatewayPaidAmount,
                    context.TotalPaidAmount > 0 ? context.TotalPaidAmount : (context.WalletPaidAmount + context.GatewayPaidAmount));

                targetWalletAmount = split.WalletRefundAmount;
                targetGatewayAmount = split.GatewayRefundAmount;
            }
            else
            {
                // Cashfree-only booking with Original Payment preference: 100% to Cashfree
                targetWalletAmount = 0m;
                targetGatewayAmount = context.RefundAmount;
            }

            // Enforce Invariants
            if (!isUserPreferWallet)
            {
                if (context.WalletPaidAmount > 0 && targetWalletAmount > context.WalletPaidAmount)
                {
                    targetWalletAmount = context.WalletPaidAmount;
                }
                if (context.GatewayPaidAmount > 0 && targetGatewayAmount > context.GatewayPaidAmount)
                {
                    targetGatewayAmount = context.GatewayPaidAmount;
                }
            }
            if (targetWalletAmount + targetGatewayAmount > context.RefundAmount)
            {
                decimal excess = (targetWalletAmount + targetGatewayAmount) - context.RefundAmount;
                if (targetGatewayAmount >= excess) targetGatewayAmount -= excess;
                else targetWalletAmount -= excess;
            }

            // 6. Execute Wallet Refund Component
            decimal actualWalletRefunded = 0m;
            string walletStatus = "NOT_REQUIRED";
            string? walletError = null;

            if (targetWalletAmount > 0m)
            {
                if (walletAlreadyRefunded)
                {
                    walletStatus = "Refunded";
                    actualWalletRefunded = cancellation!.WalletRefundAmount > 0 ? cancellation.WalletRefundAmount : targetWalletAmount;
                    _logger.LogInformation("Wallet component of {Amount} already refunded for {Ref}. Skipping duplicate.", actualWalletRefunded, context.BookingReference);
                }
                else
                {
                    try
                    {
                        string refType = $"{context.BookingType}Refund";
                        string description = $"Refund for cancelled {context.BookingType} booking: {context.BookingReference}";

                        var tx = await _walletService.RefundAsync(
                            context.UserId,
                            targetWalletAmount,
                            refType,
                            context.BookingReference,
                            description);

                        walletStatus = "Refunded";
                        actualWalletRefunded = targetWalletAmount;
                        _logger.LogInformation("Successfully refunded {Amount} to Wallet for User {UserId}, Ref: {Ref}", actualWalletRefunded, context.UserId, context.BookingReference);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to credit wallet refund of {Amount} for {Ref}", targetWalletAmount, context.BookingReference);
                        walletStatus = "Failed";
                        walletError = ex.Message;
                    }
                }
            }

            // 7. Execute Gateway Refund Component
            decimal actualGatewayRefunded = 0m;
            string gatewayStatus = "NOT_REQUIRED";
            string? gatewayError = null;
            string? refundId = cancellation?.CashfreeRefundId;

            if (targetGatewayAmount > 0m)
            {
                if (gatewayAlreadyInitiated)
                {
                    gatewayStatus = audit.GatewayStatus != "NOT_REQUIRED" ? audit.GatewayStatus : "Processing";
                    actualGatewayRefunded = cancellation!.GatewayRefundAmount > 0 ? cancellation.GatewayRefundAmount : targetGatewayAmount;
                    refundId = cancellation.CashfreeRefundId;
                    _logger.LogInformation("Gateway refund of {Amount} was already initiated/completed for {Ref} with RefundId: {RefundId}. Skipping duplicate call.",
                        actualGatewayRefunded, context.BookingReference, refundId);
                }
                else if (string.IsNullOrWhiteSpace(context.CashfreeOrderId))
                {
                    _logger.LogWarning("Cannot issue gateway refund for {Ref}: CashfreeOrderId is missing.", context.BookingReference);
                    gatewayStatus = "Failed";
                    gatewayError = "CashfreeOrderId is missing.";
                }
                else
                {
                    refundId ??= $"REF-CAN-{context.BookingReference}-G";
                    if (refundId.Length > 40)
                    {
                        // Use a hash suffix to prevent truncation collisions between different booking references
                        using var sha = System.Security.Cryptography.SHA256.Create();
                        var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(refundId));
                        string hashSuffix = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);
                        refundId = refundId.Substring(0, 31) + "-" + hashSuffix;
                    }

                    try
                    {
                        var doc = await _cashfreeService.InitiateRefundAsync(
                            context.CashfreeOrderId,
                            targetGatewayAmount,
                            refundId,
                            context.Reason ?? "Customer Cancellation");

                        string cfStatus = "PENDING";
                        if (doc != null && doc.RootElement.TryGetProperty("refund_status", out var cfProp))
                        {
                            cfStatus = cfProp.GetString() ?? "PENDING";
                        }

                        if (string.Equals(cfStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                        {
                            gatewayStatus = "Refunded";
                        }
                        else
                        {
                            gatewayStatus = "Processing";
                        }
                        actualGatewayRefunded = targetGatewayAmount;
                        _logger.LogInformation("Cashfree refund of {Amount} initiated for Order {OrderId} with RefundId {RefundId}, GatewayStatus: {Status}",
                            actualGatewayRefunded, context.CashfreeOrderId, refundId, gatewayStatus);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed Cashfree refund of {Amount} for Order {OrderId}", targetGatewayAmount, context.CashfreeOrderId);
                        gatewayStatus = "Failed";
                        gatewayError = ex.Message;
                    }
                }
            }

            // 8. Determine Overall Refund Status & Component Aggregation
            string overallRefundStatus;
            string cancellationDbStatus;

            bool hasFailedComponent = walletStatus == "Failed" || gatewayStatus == "Failed";
            bool hasSuccessComponent = walletStatus == "Refunded" || gatewayStatus == "Refunded" || gatewayStatus == "Processing";

            if (hasFailedComponent)
            {
                if (hasSuccessComponent)
                {
                    overallRefundStatus = "PartiallyRefunded";
                    cancellationDbStatus = "PartiallyRefunded";
                }
                else
                {
                    overallRefundStatus = "Failed";
                    cancellationDbStatus = "Failed";
                }
            }
            else
            {
                // Neither component failed
                bool isWalletDone = targetWalletAmount == 0m || walletStatus == "Refunded";
                bool isGatewayDone = targetGatewayAmount == 0m || gatewayStatus == "Refunded";

                if (isWalletDone && isGatewayDone)
                {
                    overallRefundStatus = "COMPLETED";
                    cancellationDbStatus = "Completed";
                }
                else
                {
                    // Gateway is submitted and in processing with bank
                    overallRefundStatus = "Processing";
                    cancellationDbStatus = "Processing";
                }
            }

            // 9. Persist Updated Component State and Ledger to BookingCancellation
            if (cancellation != null)
            {
                cancellation.RefundPreference = isUserPreferWallet ? "WALLET" : (isWalletPayment ? "WALLET" : "ORIGINAL_PAYMENT_METHOD");
                cancellation.WalletRefundAmount = actualWalletRefunded;
                cancellation.GatewayRefundAmount = actualGatewayRefunded;
                cancellation.CashfreeRefundId = refundId ?? cancellation.CashfreeRefundId;
                cancellation.RefundStatus = overallRefundStatus;
                cancellation.Status = cancellationDbStatus;
                if (cancellationDbStatus == "Completed")
                {
                    cancellation.CompletedAtUtc = DateTime.UtcNow;
                }

                var compAudit = new ComponentRefundAudit
                {
                    WalletStatus = walletStatus,
                    GatewayStatus = gatewayStatus,
                    WalletRefundAmount = actualWalletRefunded,
                    GatewayRefundAmount = actualGatewayRefunded,
                    WalletError = walletError,
                    GatewayError = gatewayError,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                cancellation.FailureReason = JsonSerializer.Serialize(compAudit);

                await _dbContext.SaveChangesAsync();

                // Additive In-App Notifications (Step 4: Refund Events)
                if (_inAppNotificationService != null)
                {
                    try
                    {
                        if (overallRefundStatus == "COMPLETED")
                        {
                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Refund",
                                category: "Customer",
                                title: "Refund Completed",
                                message: BuildResultMessage(overallRefundStatus, walletStatus, gatewayStatus, actualWalletRefunded, actualGatewayRefunded, null),
                                severity: "Success",
                                referenceType: "BookingCancellation",
                                referenceId: cancellation.Id.ToString(),
                                actionUrl: $"/bookings/{cancellation.BookingReference}",
                                idempotencyKey: $"REFUND_DONE_{cancellation.Id}",
                                targetUserId: cancellation.UserId
                            );

                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "WALLET_REFUND",
                                category: "FINANCIAL",
                                title: "Refund Completed",
                                message: $"Refund completed for booking {cancellation.BookingReference} (Wallet: INR {actualWalletRefunded:N2}, Gateway: INR {actualGatewayRefunded:N2}).",
                                severity: "INFO",
                                referenceType: "BookingCancellation",
                                referenceId: cancellation.Id.ToString(),
                                actionUrl: $"/admin/refunds/{cancellation.Id}",
                                idempotencyKey: $"ADMIN_REFUND_SUCCESS_{cancellation.Id}",
                                targetRole: "ADMIN"
                            );
                        }
                        else if (overallRefundStatus == "PartiallyRefunded")
                        {
                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Refund",
                                category: "Customer",
                                title: "Partial Refund Processed",
                                message: $"Your refund for {cancellation.BookingReference} was partially completed. Remaining amount is being processed.",
                                severity: "Warning",
                                referenceType: "BookingCancellation",
                                referenceId: cancellation.Id.ToString(),
                                actionUrl: $"/bookings/{cancellation.BookingReference}",
                                idempotencyKey: $"REFUND_PARTIAL_{cancellation.Id}",
                                targetUserId: cancellation.UserId
                            );

                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "WALLET_REFUND",
                                category: "FINANCIAL",
                                title: "Partial Refund Needs Attention",
                                message: $"Partial refund for {cancellation.BookingReference} (Wallet: ₹{actualWalletRefunded:N2}, Gateway: ₹{actualGatewayRefunded:N2}).",
                                severity: "Warning",
                                referenceType: "BookingCancellation",
                                referenceId: cancellation.Id.ToString(),
                                actionUrl: $"/admin/refunds/{cancellation.Id}",
                                idempotencyKey: $"ADMIN_REFUND_PARTIAL_{cancellation.Id}",
                                targetRole: "ADMIN"
                            );
                        }
                        else if (overallRefundStatus == "Failed")
                        {
                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Refund",
                                category: "Customer",
                                title: "Refund Processing Issue",
                                message: $"We encountered an issue processing your refund for {cancellation.BookingReference}. Our support team has been notified.",
                                severity: "Error",
                                referenceType: "BookingCancellation",
                                referenceId: cancellation.Id.ToString(),
                                actionUrl: $"/bookings/{cancellation.BookingReference}",
                                idempotencyKey: $"REFUND_FAIL_{cancellation.Id}",
                                targetUserId: cancellation.UserId
                            );

                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "WALLET_REFUND",
                                category: "FINANCIAL",
                                title: "Refund Failed",
                                message: $"Refund for booking {cancellation.BookingReference} failed: {walletError ?? gatewayError ?? "Unknown error"}",
                                severity: "ERROR",
                                referenceType: "BookingCancellation",
                                referenceId: cancellation.Id.ToString(),
                                actionUrl: $"/admin/refunds/{cancellation.Id}",
                                idempotencyKey: $"ADMIN_REFUND_FAIL_{cancellation.Id}",
                                targetRole: "ADMIN"
                            );
                        }
                    }
                    catch (Exception inAppEx)
                    {
                        _logger.LogWarning(inAppEx, "Failed to create in-app notification for refund cancellation {CancellationId}. Non-fatal.", cancellation.Id);
                    }
                }
            }

            return new CancellationRefundResultDto
            {
                RefundStatus = overallRefundStatus,
                RefundDestination = isUserPreferWallet ? "WALLET" : (isWalletPayment ? "WALLET" : (isHybridPayment ? "HYBRID" : "ORIGINAL_PAYMENT_METHOD")),
                WalletRefunded = actualWalletRefunded,
                GatewayRefunded = actualGatewayRefunded,
                WalletStatus = walletStatus,
                GatewayStatus = gatewayStatus,
                CashfreeRefundId = refundId,
                Message = BuildResultMessage(overallRefundStatus, walletStatus, gatewayStatus, actualWalletRefunded, actualGatewayRefunded, walletError ?? gatewayError)
            };
        }

        private static ComponentRefundAudit ParseComponentAudit(string? raw)
        {
            if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith("{"))
            {
                try
                {
                    return JsonSerializer.Deserialize<ComponentRefundAudit>(raw) ?? new ComponentRefundAudit();
                }
                catch
                {
                    // Ignore parsing error, return fresh audit
                }
            }
            return new ComponentRefundAudit();
        }

        private static string BuildResultMessage(string overallStatus, string walletStatus, string gatewayStatus, decimal walletAmt, decimal gatewayAmt, string? error)
        {
            if (overallStatus == "COMPLETED")
            {
                if (walletAmt > 0 && gatewayAmt > 0)
                {
                    return $"Refund completed: ₹{walletAmt:N2} credited to Pick&book Wallet and ₹{gatewayAmt:N2} settled to original payment method.";
                }
                if (walletAmt > 0)
                {
                    return $"Refund of ₹{walletAmt:N2} credited directly to your Pick&book wallet.";
                }
                return $"Refund of ₹{gatewayAmt:N2} completed to original payment method.";
            }

            if (overallStatus == "PartiallyRefunded")
            {
                return $"Partial refund completed (Wallet: ₹{walletAmt:N2} [{walletStatus}], Gateway: ₹{gatewayAmt:N2} [{gatewayStatus}]). Pending component will be retried.";
            }

            if (overallStatus == "Processing")
            {
                if (walletAmt > 0 && gatewayAmt > 0)
                {
                    return $"₹{walletAmt:N2} credited to Pick&book Wallet. ₹{gatewayAmt:N2} initiated to original payment method (takes 3-5 business days).";
                }
                if (gatewayAmt > 0)
                {
                    return $"Refund of ₹{gatewayAmt:N2} initiated to original payment method. Settlement typically takes 3-5 business days.";
                }
                return "Refund processing.";
            }

            return $"Refund failed: {error ?? "Unknown error"}. Please retry or contact customer support.";
        }
    }
}
