using System;

namespace PickNBook.Api.Models;

public class TestimonialSetting
{
    public int Id { get; set; } = 1;
    public bool ApprovalRequired { get; set; } = true;
    public bool AllowUserSubmission { get; set; } = true;
    public bool AllowRating { get; set; } = true;
    public bool AllowCustomerImage { get; set; } = true;
    public string DefaultCategory { get; set; } = "Hotel Stay";
    public int FeaturedLimit { get; set; } = 6;
    public string DisplayOrderMode { get; set; } = "Manual Order";
    public bool AutoPublish { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
