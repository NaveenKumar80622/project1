using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs;

public class CreateCustomerRequest
{
    [Required(ErrorMessage = "First Name is required.")]
    [MaxLength(100, ErrorMessage = "First Name cannot exceed 100 characters.")]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "Last Name cannot exceed 100 characters.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email ID is required.")]
    [EmailAddress(ErrorMessage = "Invalid Email Address.")]
    [MaxLength(150, ErrorMessage = "Email ID cannot exceed 150 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mobile number is required.")]
    [MaxLength(20, ErrorMessage = "Mobile number cannot exceed 20 characters.")]
    public string Mobile { get; set; } = string.Empty;

    [MaxLength(20, ErrorMessage = "Alternate Mobile number cannot exceed 20 characters.")]
    public string? AltMobile { get; set; }

    [RegularExpression("^(Male|Female|Other)$", ErrorMessage = "Gender must be Male, Female, or Other.")]
    public string Gender { get; set; } = "Male";

    [RegularExpression("^(Active|Inactive)$", ErrorMessage = "Status must be Active or Inactive.")]
    public string Status { get; set; } = "Active";

    [RegularExpression("^(Active|Inactive)$", ErrorMessage = "Wallet Status must be Active or Inactive.")]
    public string WalletStatus { get; set; } = "Active";

    [MaxLength(100, ErrorMessage = "Login ID cannot exceed 100 characters.")]
    public string? LoginId { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("referredBy")]
    public string? RefferedBy { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? Remark { get; set; }
    public string? AadharNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? PanName { get; set; }
}

public class AddWalletBalanceRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }
}

public class CustomerResponseDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string EmailId { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string WalletStatus { get; set; } = string.Empty;
    public decimal WalletBalance { get; set; }
    public string? AltMobile { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? LoginId { get; set; }
    [JsonPropertyName("referredBy")]
    public string? RefferedBy { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? Remark { get; set; }
    public string? AadharNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? PanName { get; set; }
    public DateTime CreatedAt { get; set; }
}
