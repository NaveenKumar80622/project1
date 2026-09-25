using System;

namespace PickNBook.Api.Models;

public class MenuItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Module { get; set; } = "B2C"; // B2C, B2B, Admin
    public string Location { get; set; } = "header"; // header, footer, sidebar
    public string Status { get; set; } = "active"; // active, inactive
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
