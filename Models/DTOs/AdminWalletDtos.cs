using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Models.DTOs
{
    public class AdminWalletLedgerRequestDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int? UserId { get; set; }
        public string? TransactionType { get; set; }
        public string? Status { get; set; }
        public string? ReferenceType { get; set; }
        public string? Search { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class AdminWalletLedgerItemDto
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal RunningBalance { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public string RefCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class AdminWalletCustomerSummaryDto
    {
        public int UserId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal WalletBalance { get; set; }
        public string WalletStatus { get; set; } = string.Empty;
        public int PicknbookCoins { get; set; }
        public decimal TotalDeposits { get; set; }
        public decimal TotalBookingSpend { get; set; }
        public decimal TotalRefunds { get; set; }
        public decimal TotalAdjustments { get; set; }
        public List<AdminWalletLedgerItemDto> RecentTransactions { get; set; } = new();
    }
}
