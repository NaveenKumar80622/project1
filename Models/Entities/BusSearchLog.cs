namespace PickNBook.Api.Models
{
    public class BusSearchLog
    {
        public int Id { get; set; }
        public string? UserId { get; set; }
        public string? UserOrGuestId { get; set; }
        public bool IsGuest { get; set; }
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public DateOnly? JourneyDate { get; set; }
        public DateTime SearchedAtUtc { get; set; }
    }
}
