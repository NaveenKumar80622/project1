namespace PickNBook.Api.Models;

public class AboutUsCount
{
    public int Id { get; set; }
    public int AboutUsId { get; set; }
    public string CountValue { get; set; } = string.Empty;
    public string CountTitle { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    // Navigation property
    public AboutUs AboutUs { get; set; } = null!;
}
