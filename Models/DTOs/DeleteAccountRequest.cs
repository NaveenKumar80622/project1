using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class DeleteAccountRequest
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(200, ErrorMessage = "Reason cannot exceed 200 characters.")]
        public string? Reason { get; set; }

        [MaxLength(1000, ErrorMessage = "Additional details cannot exceed 1000 characters.")]
        public string? AdditionalDetails { get; set; }
    }
}
