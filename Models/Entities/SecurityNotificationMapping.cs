using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_notification_mappings")]
    public class SecurityNotificationMapping
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

        [Column("template_name")]
        [MaxLength(100)]
        [Required]
        public string TemplateName { get; set; } = string.Empty;

        [Column("enabled")]
        public bool Enabled { get; set; } = true;

        [Column("cooldown_seconds")]
        public int CooldownSeconds { get; set; } = 60;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_by")]
        [MaxLength(64)]
        public string? UpdatedBy { get; set; }
    }
}
