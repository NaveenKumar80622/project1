using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_settings")]
    public class SecuritySettings
    {
        [Key]
        [Column("id")]
        public int Id { get; set; } = 1;

        [Column("settings_json")]
        [Required]
        public string SettingsJson { get; set; } = "{}";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_by")]
        [MaxLength(64)]
        public string? UpdatedBy { get; set; }
    }
}
