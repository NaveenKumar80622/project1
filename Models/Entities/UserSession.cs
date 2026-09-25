using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("user_sessions")]
    public class UserSession
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("session_id")]
        [MaxLength(64)]
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

        [Column("user_id")]
        [MaxLength(64)]
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Column("status")]
        [MaxLength(10)]
        [Required]
        public string Status { get; set; } = "ACTIVE";

        [Column("ip_address")]
        [MaxLength(45)]
        [Required]
        public string IpAddress { get; set; } = string.Empty;

        [Column("user_agent")]
        public string? UserAgent { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("last_activity_at")]
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }
    }
}
