namespace PickNBook.Api.Models;

public class OfferSubscriber
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? WhatsAppNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime SubscribedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
