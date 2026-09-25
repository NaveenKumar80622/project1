using System;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models
{
    public class UserLockout
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public int FailedAttempts { get; set; }
        public int MaxAllowedAttempts { get; set; }
        
        public DateTime LockedOn { get; set; } = DateTime.UtcNow;
        public DateTime UnlockAt { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = "Locked"; // Locked, Unlocked
    }
}
