using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class SendRegistrationOtpRequest
    {
        [EmailAddress]
        public string? Email { get; set; }

        [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Invalid phone number format.")]
        public string? PhoneNumber { get; set; }

        public string Channel { get; set; } = "Email"; // "Email" or "Mobile"
    }
}
