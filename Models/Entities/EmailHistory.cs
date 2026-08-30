using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("email_history")]
    public class EmailHistory
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("recipient_email")]
        [MaxLength(255)]
        [Required]
        public string RecipientEmail { get; set; } = string.Empty;

        [Column("template_id")]
        public int? TemplateId { get; set; }

        [Column("subject")]
        [MaxLength(255)]
        [Required]
        public string Subject { get; set; } = string.Empty;

        [Column("scope")]
        [MaxLength(20)]
        public string Scope { get; set; } = "SYSTEM";

        [Column("security_event")]
        [MaxLength(50)]
        public string? SecurityEvent { get; set; }

        [Column("email_type")]
        [MaxLength(50)]
        public string EmailType { get; set; } = string.Empty; // Security, Manual, Reminder, Test

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "Sent"; // Sent, Failed

        [Column("delivery_status")]
        [MaxLength(20)]
        public string DeliveryStatus { get; set; } = "PENDING"; // PENDING, SENT, FAILED, RETRY, CANCELLED

        [Column("retry_count")]
        public int RetryCount { get; set; } = 0;

        [Column("error_message", TypeName = "longtext")]
        public string? ErrorMessage { get; set; }

        [Column("sent_at")]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [Column("created_by")]
        [MaxLength(100)]
        public string? CreatedBy { get; set; }
    }
}
