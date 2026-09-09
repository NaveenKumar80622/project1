using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class RefundRouterService : IRefundRouterService
    {
        private readonly IWalletService _walletService;
        private readonly ICashfreeService _cashfreeService;
        private readonly ILogger<RefundRouterService> _logger;

        public RefundRouterService(
            IWalletService walletService,
            ICashfreeService cashfreeService,
            ILogger<RefundRouterService> logger)
        {
            _walletService = walletService;
            _cashfreeService = cashfreeService;
            _logger = logger;
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
                    Message = "Refund amount is 0. No refund needed."
                };
            }

            bool isWalletPayment = string.Equals(context.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase);
            bool isUserPreferWallet = string.Equals(context.RefundPreference, "WALLET", StringComparison.OrdinalIgnoreCase);

            // Rule 1: Wallet Origin Override
            // If the booking was paid with Wallet, refund MUST go back to Wallet
            if (isWalletPayment || isUserPreferWallet)
            {
                _logger.LogInformation("Routing refund of {Amount} to User Wallet for User {UserId}, Ref: {BookingRef}. Origin was {PaymentMethod}, Preference was {Pref}",
                    context.RefundAmount, context.UserId, context.BookingReference, context.PaymentMethod, context.RefundPreference);

                string refType = $"{context.BookingType}Refund";
                string description = $"Refund for cancelled {context.BookingType} booking: {context.BookingReference}";

                var tx = await _walletService.RefundAsync(
                    context.UserId,
                    context.RefundAmount,
                    refType,
                    context.BookingReference,
                    description);

                return new CancellationRefundResultDto
                {
                    RefundStatus = "COMPLETED",
                    RefundDestination = "WALLET",
                    WalletRefunded = context.RefundAmount,
                    GatewayRefunded = 0m,
                    Message = "Refund credited directly to your PickNBook wallet instantly."
                };
            }

            // Rule 2: Original Payment Method (Cashfree Gateway Refund)
            _logger.LogInformation("Routing refund of {Amount} to Original Payment Method (Cashfree) for Ref: {BookingRef}, OrderId: {OrderId}",
                context.RefundAmount, context.BookingReference, context.CashfreeOrderId);

            if (string.IsNullOrWhiteSpace(context.CashfreeOrderId))
            {
                _logger.LogWarning("Cannot issue gateway refund for {BookingRef}: CashfreeOrderId is missing. Defaulting refund to wallet.", context.BookingReference);
                
                var fallbackTx = await _walletService.RefundAsync(
                    context.UserId,
                    context.RefundAmount,
                    $"{context.BookingType}Refund",
                    context.BookingReference,
                    $"Refund fallback to wallet for {context.BookingType} booking {context.BookingReference}");

                return new CancellationRefundResultDto
                {
                    RefundStatus = "COMPLETED",
                    RefundDestination = "WALLET",
                    WalletRefunded = context.RefundAmount,
                    GatewayRefunded = 0m,
                    Message = "Gateway order reference missing. Credited refund to your PickNBook wallet."
                };
            }

            string refundId = $"REF-CAN-{context.BookingReference}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            if (refundId.Length > 40)
            {
                refundId = refundId.Substring(0, 40);
            }

            try
            {
                var doc = await _cashfreeService.InitiateRefundAsync(
                    context.CashfreeOrderId,
                    context.RefundAmount,
                    refundId,
                    context.Reason ?? "Booking Cancellation");

                return new CancellationRefundResultDto
                {
                    RefundStatus = "PENDING",
                    RefundDestination = "ORIGINAL_PAYMENT_METHOD",
                    GatewayRefunded = context.RefundAmount,
                    WalletRefunded = 0m,
                    CashfreeRefundId = refundId,
                    Message = "Refund initiated to original payment method. May take 3-5 business days depending on your bank."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch Cashfree refund for Order {OrderId}", context.CashfreeOrderId);
                return new CancellationRefundResultDto
                {
                    RefundStatus = "FAILED",
                    RefundDestination = "ORIGINAL_PAYMENT_METHOD",
                    GatewayRefunded = 0m,
                    WalletRefunded = 0m,
                    CashfreeRefundId = refundId,
                    Message = $"Failed to initiate gateway refund: {ex.Message}"
                };
            }
        }
    }
}
