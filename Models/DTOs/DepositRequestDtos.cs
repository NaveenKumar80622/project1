using System;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs;

public class DepositRequestDto
{
    public long Id { get; set; }
    public string User { get; set; } = string.Empty; // Format: "Customer Name (UserId)"
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty; // Cash, NEFT
    public string Status { get; set; } = string.Empty; // Pending, Approved, Rejected
    public string? UserRemark { get; set; }
    public string? AdminRemark { get; set; }
    public string EntryDate { get; set; } = string.Empty; // formatted string matching frontend
    public string TransactionDate { get; set; } = string.Empty;
}

public class UpdateAdminRemarkRequest
{
    [Required(ErrorMessage = "Admin remark is required.")]
    public string AdminRemark { get; set; } = string.Empty;
}
