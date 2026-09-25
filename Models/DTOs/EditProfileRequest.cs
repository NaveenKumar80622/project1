using Microsoft.AspNetCore.Http;

public class EditProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Location { get; set; }
    public IFormFile? ProfileImage { get; set; }
}

public class EditProfileJsonRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Location { get; set; }
    public string? ProfileImage { get; set; }
}
