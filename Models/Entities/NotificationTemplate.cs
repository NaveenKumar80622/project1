using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("notification_templates")]
    public class NotificationTemplate
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("template_key")]
        public string TemplateKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Column("event_type")]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("channel")]
        public string Channel { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        [Column("language")]
        public string Language { get; set; } = "en";

        [MaxLength(255)]
        [Column("subject")]
        public string? Subject { get; set; }

        [Required]
        [Column("body", TypeName = "text")]
        public string Body { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("provider_template_id")]
        public string? ProviderTemplateId { get; set; }

        [MaxLength(100)]
        [Column("provider_template_name")]
        public string? ProviderTemplateName { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
