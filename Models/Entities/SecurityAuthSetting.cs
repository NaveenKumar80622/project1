using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_auth_settings")]
    public class SecurityAuthSetting
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = string.Empty; // ADMIN, USER, B2B

        [Column("category")]
        [MaxLength(50)]
        [Required]
        public string Category { get; set; } = string.Empty; // LOGIN, OTP, REGISTRATION, FORGOT_PASSWORD, PASSWORD

        [Column("settings_json", TypeName = "json")]
        [Required]
        public string SettingsJson { get; set; } = "{}";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
