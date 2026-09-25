using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("in_app_notifications")]
    public class InAppNotification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "text")]
        public string Message { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Severity { get; set; } = "Info";

        [MaxLength(100)]
        public string? ReferenceType { get; set; }

        [MaxLength(100)]
        public string? ReferenceId { get; set; }

        [MaxLength(500)]
        public string? ActionUrl { get; set; }

        [MaxLength(150)]
        public string? IdempotencyKey { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Relationship: 1 InAppNotification -> many NotificationRecipient
        public ICollection<NotificationRecipient> Recipients { get; set; } = new List<NotificationRecipient>();
    }
}
