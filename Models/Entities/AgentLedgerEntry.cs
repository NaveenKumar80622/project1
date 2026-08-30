using System;

namespace PickNBook.Api.Models
{
    public class AgentLedgerEntry
    {
        public long Id { get; set; }
        public int AgentId { get; set; }
        public User? Agent { get; set; }
        public string TransactionType { get; set; } = string.Empty; // Deposit, Booking, Refund, Adjustment
        public string ReferenceId { get; set; } = string.Empty; // BookingReference or DepositRequest ID
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal RunningBalance { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
