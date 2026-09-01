using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class SendLoginOtpRequest
    {
        [Required(ErrorMessage = "Phone number is required")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Invalid phone number format. Must be 10 digits.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public class VerifyLoginOtpRequest
    {
        [Required(ErrorMessage = "Phone number is required")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "OTP is required")]
        public string Otp { get; set; } = string.Empty;
    }
}
