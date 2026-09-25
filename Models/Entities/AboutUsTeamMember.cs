namespace PickNBook.Api.Models;

public class AboutUsTeamMember
{
    public int Id { get; set; }
    public int AboutUsId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    // Navigation property
    public AboutUs AboutUs { get; set; } = null!;
}
