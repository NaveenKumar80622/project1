using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models;

public class AboutUs
{
    public int Id { get; set; }
    public string AboutDescription { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string Module { get; set; } = "All";
    public string WhoWeAreHeading { get; set; } = string.Empty;
    public string WhoWeAreDescription { get; set; } = string.Empty;
    public string WhoWeAreImageUrl { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public List<AboutUsCount> Counts { get; set; } = new();
    public List<AboutUsTeamMember> TeamMembers { get; set; } = new();
}
