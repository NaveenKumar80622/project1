namespace PickNBook.Api.Models.DTOs
{
    public class UserProfileDto
    {
        public int UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Location { get; set; }
        public string? ProfileImage { get; set; }
        public decimal WalletBalance { get; set; }
        public string? WalletStatus { get; set; }
    }
}
