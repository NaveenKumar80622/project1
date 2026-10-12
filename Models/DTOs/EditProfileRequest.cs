using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PickNBook.Api.Models.DTOs;

public class EditProfileRequest
{
    [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters.")]
    [RegularExpression(@"^[a-zA-Z\s.'-]+$", ErrorMessage = "First name must contain only letters and cannot contain numbers or special characters.")]
    public string? FirstName { get; set; }

    [StringLength(50, MinimumLength = 1, ErrorMessage = "Last name cannot exceed 50 characters.")]
    [RegularExpression(@"^[a-zA-Z\s.'-]+$", ErrorMessage = "Last name must contain only letters and cannot contain numbers or special characters.")]
    public string? LastName { get; set; }

    [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Phone number must be a valid 10-digit Indian mobile number.")]
    public string? PhoneNumber { get; set; }

    [StringLength(100, MinimumLength = 2, ErrorMessage = "Location cannot exceed 100 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\s,.'-]+$", ErrorMessage = "Location contains invalid characters.")]
    public string? Location { get; set; }

    /// <summary>
    /// Optional profile image file (.jpg, .jpeg, .png, .webp up to 5MB).
    /// </summary>
    public IFormFile? ProfileImage { get; set; }
}

public class EditProfileJsonRequest
{
    [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters.")]
    [RegularExpression(@"^[a-zA-Z\s.'-]+$", ErrorMessage = "First name must contain only letters and cannot contain numbers or special characters.")]
    public string? FirstName { get; set; }

    [StringLength(50, MinimumLength = 1, ErrorMessage = "Last name cannot exceed 50 characters.")]
    [RegularExpression(@"^[a-zA-Z\s.'-]+$", ErrorMessage = "Last name must contain only letters and cannot contain numbers or special characters.")]
    public string? LastName { get; set; }

    [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Phone number must be a valid 10-digit Indian mobile number.")]
    public string? PhoneNumber { get; set; }

    [StringLength(100, MinimumLength = 2, ErrorMessage = "Location cannot exceed 100 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\s,.'-]+$", ErrorMessage = "Location contains invalid characters.")]
    public string? Location { get; set; }

    [MaxLength(255, ErrorMessage = "Profile image path cannot exceed 255 characters.")]
    [RegularExpression(@"^(/profile-images/[a-zA-Z0-9_-]+\.(jpg|jpeg|png|webp)|https?://[a-zA-Z0-9.-]+(?::[0-9]+)?(/[a-zA-Z0-9_.\-%]*)*)$", ErrorMessage = "Profile image must be a valid image path or URL.")]
    public string? ProfileImage { get; set; }
}

