using System;

namespace PickNBook.Api.Models.DTOs
{
    public class WalletSummaryDto
    {
        public decimal AvailableBalance { get; set; }
        public int PicknbookCoins { get; set; }
        public string WalletStatus { get; set; } = "Active";
        public decimal TotalAdded { get; set; }
        public decimal TotalRefunded { get; set; }
        public decimal TotalUsed { get; set; }
    }

    public class WalletTransactionDto
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string TransactionType { get; set; } = string.Empty; // "Credit", "Debit", "Refund"
        public decimal Amount { get; set; }
        public decimal RunningBalance { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public string RefCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class SubmitDepositRequestDto
    {
        public decimal Amount { get; set; }
        public string Type { get; set; } = "Bank Transfer"; // Bank Transfer, UPI, IMPS, NEFT, Cheque
        public string? Remark { get; set; }
        public DateTime? TransactionDate { get; set; }
    }

    public class CancellationRefundResultDto
    {
        public string RefundStatus { get; set; } = "PENDING"; // COMPLETED, PENDING, FAILED
        public string RefundDestination { get; set; } = "ORIGINAL_PAYMENT_METHOD"; // WALLET, ORIGINAL_PAYMENT_METHOD
        public decimal WalletRefunded { get; set; }
        public decimal GatewayRefunded { get; set; }
        public string? CashfreeRefundId { get; set; }
        public string? Message { get; set; }
    }

    public class BusCancelRequestDto
    {
        public string? Reason { get; set; }
        public string? RefundPreference { get; set; } // "WALLET" or "ORIGINAL_PAYMENT_METHOD"
    }

    public class PublicHotelCancelRequestDto
    {
        public string? Reason { get; set; }
        public string? RefundPreference { get; set; } // "WALLET" or "ORIGINAL_PAYMENT_METHOD"
    }
}
