using System;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    /// <summary>
    /// Implements proportional split refund routing for Hybrid bookings:
    /// Wallet Ratio = WalletPaidAmount / TotalPaidAmount
    /// Gateway Ratio = GatewayPaidAmount / TotalPaidAmount
    /// Enforces strict invariants:
    /// 1. WalletRefundAmount + GatewayRefundAmount == NetRefundAmount
    /// 2. WalletRefundAmount <= WalletPaidAmount
    /// 3. GatewayRefundAmount <= GatewayPaidAmount
    /// 4. Never refunds more than the authoritative refundable amount
    /// 5. Standard 2-decimal rounding with AwayFromZero midpoint rounding
    /// </summary>
    public class ProportionalHybridRefundSplitStrategy : IHybridRefundSplitStrategy
    {
        public HybridRefundSplit CalculateSplit(decimal refundableAmount, decimal walletPaidAmount, decimal gatewayPaidAmount, decimal totalPaidAmount)
        {
            if (refundableAmount <= 0m || totalPaidAmount <= 0m)
            {
                return new HybridRefundSplit
                {
                    WalletRefundAmount = 0m,
                    GatewayRefundAmount = 0m
                };
            }

            // Cap refundable amount to total paid and sum of components
            decimal maxRefundable = Math.Min(totalPaidAmount, walletPaidAmount + gatewayPaidAmount);
            decimal effectiveRefundable = Math.Min(refundableAmount, maxRefundable);

            if (effectiveRefundable <= 0m)
            {
                return new HybridRefundSplit
                {
                    WalletRefundAmount = 0m,
                    GatewayRefundAmount = 0m
                };
            }

            // Calculate proportional wallet refund
            decimal walletRatio = walletPaidAmount / totalPaidAmount;
            decimal walletRefund = Math.Round(effectiveRefundable * walletRatio, 2, MidpointRounding.AwayFromZero);

            // Cap wallet refund to original wallet component paid
            if (walletRefund > walletPaidAmount)
            {
                walletRefund = walletPaidAmount;
            }

            // Gateway refund takes the remaining exact balance to satisfy the exact sum invariant
            decimal gatewayRefund = effectiveRefundable - walletRefund;

            // Cap gateway refund to original gateway component paid
            if (gatewayRefund > gatewayPaidAmount)
            {
                gatewayRefund = gatewayPaidAmount;
                walletRefund = Math.Min(effectiveRefundable - gatewayRefund, walletPaidAmount);
            }

            // Final invariant safety check
            decimal totalAllocated = walletRefund + gatewayRefund;
            if (totalAllocated != effectiveRefundable)
            {
                decimal diff = effectiveRefundable - totalAllocated;
                if (diff > 0)
                {
                    if (gatewayRefund + diff <= gatewayPaidAmount)
                    {
                        gatewayRefund += diff;
                    }
                    else if (walletRefund + diff <= walletPaidAmount)
                    {
                        walletRefund += diff;
                    }
                }
            }

            return new HybridRefundSplit
            {
                WalletRefundAmount = walletRefund,
                GatewayRefundAmount = gatewayRefund
            };
        }
    }
}
