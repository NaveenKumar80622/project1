namespace PickNBook.Api.Models;

public class WhatsAppSettings
{
    public bool Enabled { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
}
