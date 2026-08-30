using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models
{
    public class RegisterRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        [RegularExpression(@"^[6-9]\d{9}$")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [RegularExpression(
    @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[^\s]{8,64}$",
    ErrorMessage = "Password must contain uppercase, lowercase, number, special character and be 8-64 characters long."
)]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string OTP { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AdminLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AdminLoginVerifyOtpRequest
    {
        public string ChallengeId { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
    }

    public class AdminResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class CreateAdminRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class B2BRegisterRequest
    {
        [Required]
        public string CompanyName { get; set; } = string.Empty;

        [Required]
        public string BusinessType { get; set; } = string.Empty;

        [Required]
        public string ContactName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Invalid phone number.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        public string Gstin { get; set; } = string.Empty;

        [Required]
        public string City { get; set; } = string.Empty;

        [Required]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[^\s]{8,64}$",
            ErrorMessage = "Password must contain uppercase, lowercase, number, special character and be 8-64 characters long."
        )]
        public string Password { get; set; } = string.Empty;
    }

    public class UpdateAgentStatusRequest
    {
        [Required]
        public string Status { get; set; } = string.Empty; // Active, Inactive, Rejected, PendingApproval
    }

    public class UpdateAgentWalletStatusRequest
    {
        [Required]
        public string WalletStatus { get; set; } = string.Empty; // Active, Inactive
    }
}
