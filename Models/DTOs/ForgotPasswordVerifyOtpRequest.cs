using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class ForgotPasswordVerifyOtpRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Otp { get; set; } = string.Empty;
    }
}
