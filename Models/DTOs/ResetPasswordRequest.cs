using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class ForgotPasswordResetRequest
    {
        [EmailAddress]
        public string? Email { get; set; }

        [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Invalid phone number format.")]
        public string? PhoneNumber { get; set; }

        [Required]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[^\s]{8,64}$",
            ErrorMessage = "Password must contain uppercase, lowercase, number, special character and be 8-64 characters long."
        )]
        public string NewPassword { get; set; } = string.Empty;
    }
}
