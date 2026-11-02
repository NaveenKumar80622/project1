using System;

namespace PickNBook.Api.Services
{
    public static class CancellationStatusMapper
    {
        public static string ResolveRefundStatus(
            string? auditRefundStatus,
            string? auditStatus,
            decimal refundAmount)
        {
            var rf = auditRefundStatus?.Trim() ?? string.Empty;
            var st = auditStatus?.Trim() ?? string.Empty;

            // 1. Explicit OnHold state (e.g. Cashfree balance shortfall)
            if (rf.Equals("ONHOLD", StringComparison.OrdinalIgnoreCase) ||
                rf.Equals("ON_HOLD", StringComparison.OrdinalIgnoreCase) ||
                rf.Equals("RefundOnHold", StringComparison.OrdinalIgnoreCase) ||
                rf.IndexOf("HOLD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                st.IndexOf("HOLD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "OnHold";
            }

            // 2. Explicit failure state
            if (rf.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
                st.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Failed";
            }

            // 3. Actively processing in Cashfree or Wallet queue
            if (rf.Equals("PROCESSING", StringComparison.OrdinalIgnoreCase) ||
                st.Equals("Processing", StringComparison.OrdinalIgnoreCase))
            {
                return "Processing";
            }

            // 4. Confirmed successful execution (Must verify refund gateway confirmed it)
            if (rf.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                rf.Equals("REFUNDED", StringComparison.OrdinalIgnoreCase) ||
                rf.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                return refundAmount > 0
                    ? "Refunded"
                    : "Completed";
            }

            // 5. Zero refund or no refund required
            if (rf.Equals("NOT_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                return "Completed";
            }

            // 6. Queued / Pending execution
            if (rf.Equals("PENDING", StringComparison.OrdinalIgnoreCase) ||
                st.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return "Pending";
            }

            // 7. Cancellation completed but refund payout not verified
            if (st.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                return refundAmount > 0
                    ? "Pending"
                    : "Completed";
            }

            // 8. Safe Fallback:
            return refundAmount > 0
                ? "Pending"
                : "Completed";
        }
    }
}
