using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("notification_outbox")]
    public class NotificationOutbox
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("event_type")]
        public string EventType { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("booking_id")]
        public string? BookingId { get; set; }

        [MaxLength(100)]
        [Column("user_id")]
        public string? UserId { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("channel")]
        public string Channel { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("recipient")]
        public string Recipient { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Column("template_key")]
        public string TemplateKey { get; set; } = string.Empty;

        [Required]
        [Column("payload_json", TypeName = "text")]
        public string PayloadJson { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "Pending";

        [Column("retry_count")]
        public int RetryCount { get; set; } = 0;

        [Column("next_retry_at")]
        public DateTime? NextRetryAt { get; set; }

        [Column("last_error", TypeName = "text")]
        public string? LastError { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("processed_at")]
        public DateTime? ProcessedAt { get; set; }
    }
}
