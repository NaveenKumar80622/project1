namespace PickNBook.Api.Models
{
    public class OTP
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string Code { get; set; } = string.Empty;

        public DateTime Expiry { get; set; }

        public bool IsUsed { get; set; } = false;

        public string Purpose { get; set; } = "PasswordReset";

        public string? ChallengeId { get; set; }

        public int FailedAttempts { get; set; } = 0;

        public User? User { get; set; }
        public string Email { get; set; } = string.Empty;

        public bool IsVerified { get; set; } = false;

        public string? PhoneNumber { get; set; }
    }
}
