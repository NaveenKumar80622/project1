using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("notification_logs")]
    public class NotificationLog
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("outbox_id")]
        public int OutboxId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("event_type")]
        public string EventType { get; set; } = string.Empty;

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

        [Column("rendered_content", TypeName = "text")]
        public string? RenderedContent { get; set; }

        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [MaxLength(255)]
        [Column("provider_message_id")]
        public string? ProviderMessageId { get; set; }

        [MaxLength(255)]
        [Column("provider_response")]
        public string? ProviderResponse { get; set; }

        [Column("error_message", TypeName = "text")]
        public string? ErrorMessage { get; set; }

        [Column("sent_at")]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [Column("delivered_at")]
        public DateTime? DeliveredAt { get; set; }
    }
}
