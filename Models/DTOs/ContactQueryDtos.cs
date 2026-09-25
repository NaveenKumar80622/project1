namespace PickNBook.Api.Models.DTOs
{
    public class CreateContactQueryRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNo { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateContactQueryStatusRequest
    {
        public string Status { get; set; } = "Resolved"; // Resolved, Pending, etc.
        public string? ReplyMessage { get; set; }
    }
}
