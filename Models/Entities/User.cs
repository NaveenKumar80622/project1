using System.ComponentModel.DataAnnotations;

public static class AuthRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string Agent = "Agent";
    public const string AdminOrSuperAdmin = Admin + "," + SuperAdmin;

    public static bool IsAdminScope(string? role)
    {
        return string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }
}

public class User
{
    public int Id { get; set; }

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = AuthRoles.User;

    public string? ProfileImageUrl { get; set; }

    public string Status { get; set; } = "Active"; // Active, Inactive
    public string WalletStatus { get; set; } = "Active"; // Active, Inactive
    public decimal WalletBalance { get; set; } = 0.00m;
    public string? AltMobile { get; set; }
    public string Gender { get; set; } = "Male";
    public string Currency { get; set; } = "INR";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? Remark { get; set; }
    public string? AadharNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? PanName { get; set; }
    public string? RefferedBy { get; set; }
    public string? LoginId { get; set; }
    public string? CompanyName { get; set; }
    public string? BusinessType { get; set; }
    public string? Gstin { get; set; }
    public string? AgentLogoUrl { get; set; }
    public decimal CreditLimit { get; set; } = 0.00m;
    public string MembershipTier { get; set; } = "Bronze"; // Bronze, Silver, Gold, Platinum

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
