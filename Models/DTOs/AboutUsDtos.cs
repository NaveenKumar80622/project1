using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs;

public class AboutUsDto
{
    public int Id { get; set; }
    public string AboutDescription { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string Module { get; set; } = "All";
    public WhoWeAreDto WhoWeAre { get; set; } = new();
    public List<AboutUsCountDto> CountSection { get; set; } = new();
    public List<TeamMemberDto> TeamMembers { get; set; } = new();
}

public class WhoWeAreDto
{
    public string Heading { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}

public class AboutUsCountDto
{
    public string CountValue { get; set; } = string.Empty;
    public string CountTitle { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public class TeamMemberDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public class UpdateAboutUsDto
{
    public string Module { get; set; } = "All";
    public string Status { get; set; } = "active";
    public string AboutDescription { get; set; } = string.Empty;
    public WhoWeAreDto WhoWeAre { get; set; } = new();
    public List<AboutUsCountDto> CountSection { get; set; } = new();
    public List<TeamMemberDto> TeamMembers { get; set; } = new();
}
