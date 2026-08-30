using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("email_templates")]
    public class EmailTemplate
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("template_name")]
        [MaxLength(100)]
        [Required]
        public string TemplateName { get; set; } = string.Empty;

        [Column("template_key")]
        [MaxLength(50)]
        [Required]
        public string TemplateKey { get; set; } = string.Empty;

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = "USER";

        [Column("security_event")]
        [MaxLength(50)]
        [Required]
        public string SecurityEvent { get; set; } = string.Empty;

        [Column("subject")]
        [MaxLength(255)]
        [Required]
        public string Subject { get; set; } = string.Empty;

        [Column("body", TypeName = "longtext")]
        [Required]
        public string Body { get; set; } = string.Empty;

        [Column("body_format")]
        [MaxLength(10)]
        public string BodyFormat { get; set; } = "Html"; // Text or Html

        [Column("include_login_link")]
        public bool IncludeLoginLink { get; set; } = false;

        [Column("login_button_text")]
        [MaxLength(50)]
        public string? LoginButtonText { get; set; }

        [Column("action_link_url")]
        [MaxLength(500)]
        public string? ActionLinkUrl { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("version")]
        public int Version { get; set; } = 1;

        [Column("created_by")]
        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_by")]
        [MaxLength(100)]
        public string? UpdatedBy { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
