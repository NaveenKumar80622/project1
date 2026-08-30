using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class ForgotPasswordResetRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string NewPassword { get; set; } = string.Empty;
    }
}
