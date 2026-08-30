using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs;

public class UpsertMenuItemRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name cannot exceed 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug is required.")]
    [MaxLength(200, ErrorMessage = "Slug cannot exceed 200 characters.")]
    public string Slug { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display title is required.")]
    [MaxLength(200, ErrorMessage = "Display title cannot exceed 200 characters.")]
    public string DisplayTitle { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Order must be a non-negative integer.")]
    public int Order { get; set; }

    [Required(ErrorMessage = "Module is required.")]
    [RegularExpression("^(B2C|B2B|Admin)$", ErrorMessage = "Module must be B2C, B2B, or Admin.")]
    public string Module { get; set; } = "B2C";

    [Required(ErrorMessage = "Location is required.")]
    [RegularExpression("^(header|footer|sidebar)$", ErrorMessage = "Location must be header, footer, or sidebar.")]
    public string Location { get; set; } = "header";

    [RegularExpression("^(active|inactive)$", ErrorMessage = "Status must be active or inactive.")]
    public string Status { get; set; } = "active";
}
