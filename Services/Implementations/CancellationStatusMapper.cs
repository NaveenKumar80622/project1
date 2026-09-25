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
            // 1. Explicit failure state
            if (string.Equals(auditRefundStatus, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(auditStatus, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Failed";
            }

            // 2. Actively processing in Cashfree or Wallet queue
            if (string.Equals(auditRefundStatus, "PROCESSING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(auditStatus, "Processing", StringComparison.OrdinalIgnoreCase))
            {
                return "Processing";
            }

            // 3. Confirmed successful execution
            if (string.Equals(auditRefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(auditRefundStatus, "REFUNDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(auditStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return refundAmount > 0
                    ? "Refunded"
                    : "Completed";
            }

            // 4. Zero refund or no refund required
            if (string.Equals(auditRefundStatus, "NOT_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                return "Completed";
            }

            // 5. Queued / Pending execution
            if (string.Equals(auditRefundStatus, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(auditStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                return "Pending";
            }

            // 6. Safe Fallback:
            // Refund amount alone does NOT prove money was refunded.
            // If positive refund exists but gateway has not confirmed, status is Pending.
            return refundAmount > 0
                ? "Pending"
                : "Completed";
        }
    }
}
