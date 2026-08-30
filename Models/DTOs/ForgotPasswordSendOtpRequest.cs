using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class ForgotPasswordSendOtpRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
