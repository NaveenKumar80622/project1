using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_notifications")]
    public class SecurityNotification
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("event_type")]
        [MaxLength(50)]
        [Required]
        public string EventType { get; set; } = string.Empty;

        [Column("template_id")]
        [MaxLength(64)]
        [Required]
        public string TemplateId { get; set; } = string.Empty;

        [Column("recipient_type")]
        [MaxLength(10)]
        [Required]
        public string RecipientType { get; set; } = "USER";

        [Column("recipient")]
        [MaxLength(255)]
        [Required]
        public string Recipient { get; set; } = string.Empty;

        [Column("user_id")]
        [MaxLength(64)]
        public string? UserId { get; set; }

        [Column("ip_address")]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [Column("subject")]
        [MaxLength(255)]
        [Required]
        public string Subject { get; set; } = string.Empty;

        [Column("status")]
        [MaxLength(15)]
        [Required]
        public string Status { get; set; } = "PENDING"; // 'PENDING' | 'SENT' | 'FAILED' | 'SUPPRESSED'

        [Column("cooldown_key")]
        [MaxLength(128)]
        public string? CooldownKey { get; set; }

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        [Column("retry_count")]
        public int RetryCount { get; set; } = 0;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("sent_at")]
        public DateTime? SentAt { get; set; }

        [Column("failed_at")]
        public DateTime? FailedAt { get; set; }
    }
}
