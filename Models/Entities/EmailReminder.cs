using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("email_reminders")]
    public class EmailReminder
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("reminder_name")]
        [MaxLength(150)]
        [Required]
        public string ReminderName { get; set; } = string.Empty;

        [Column("recipient_email")]
        [MaxLength(255)]
        [Required]
        public string RecipientEmail { get; set; } = string.Empty;

        [Column("template_id")]
        public int? TemplateId { get; set; }

        [Column("subject")]
        [MaxLength(255)]
        public string? Subject { get; set; }

        [Column("message", TypeName = "longtext")]
        public string? Message { get; set; }

        [Column("include_login_link")]
        public bool IncludeLoginLink { get; set; } = false;

        [Column("scheduled_time")]
        [Required]
        public DateTime ScheduledTime { get; set; }

        [Column("reminder_before_expiry_minutes")]
        public int? ReminderBeforeExpiryMinutes { get; set; }

        [Column("max_reminder_count")]
        public int MaxReminderCount { get; set; } = 1;

        [Column("current_reminder_count")]
        public int CurrentReminderCount { get; set; } = 0;

        [Column("related_block_id")]
        public long? RelatedBlockId { get; set; }

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Sent, Failed, Cancelled

        [Column("sent_at")]
        public DateTime? SentAt { get; set; }

        [Column("created_by")]
        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
