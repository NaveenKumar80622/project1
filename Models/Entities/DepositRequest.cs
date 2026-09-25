using System;

namespace PickNBook.Api.Models;

public class DepositRequest
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "NEFT"; // NEFT, Cash
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public string? UserRemark { get; set; }
    public string? AdminRemark { get; set; }
    public DateTime EntryDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
}
