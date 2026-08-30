using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_limits")]
    public class SecurityLimit
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = string.Empty; // ADMIN, USER, B2B

        [Column("rule_key")]
        [MaxLength(50)]
        [Required]
        public string RuleKey { get; set; } = string.Empty;

        [Column("rule_name")]
        [MaxLength(100)]
        [Required]
        public string RuleName { get; set; } = string.Empty;

        [Column("is_enabled")]
        public bool IsEnabled { get; set; } = true;

        [Column("limit_value")]
        public int LimitValue { get; set; } = 5;

        [Column("time_period_value")]
        public int? TimePeriodValue { get; set; } = 10;

        [Column("time_period_unit")]
        [MaxLength(20)]
        public string? TimePeriodUnit { get; set; } = "MINUTES"; // MINUTES, HOURS, DAY

        [Column("account_action")]
        [MaxLength(50)]
        public string? AccountAction { get; set; } = "NONE"; // NONE, TEMPORARY_LOCK, PERMANENT_LOCK, SUSPEND

        [Column("ip_action")]
        [MaxLength(50)]
        public string? IpAction { get; set; } = "NONE"; // NONE, BLACKLIST, BLOCK

        [Column("block_duration_value")]
        public int? BlockDurationValue { get; set; } = 60;

        [Column("block_duration_unit")]
        [MaxLength(20)]
        public string? BlockDurationUnit { get; set; } = "MINUTES"; // MINUTES, HOURS, DAYS

        [Column("email_enabled")]
        public bool EmailEnabled { get; set; } = true;

        [Column("email_template_id")]
        public long? EmailTemplateId { get; set; }

        [Column("reset_period_value")]
        public int? ResetPeriodValue { get; set; } = 10;

        [Column("reset_period_unit")]
        [MaxLength(20)]
        public string? ResetPeriodUnit { get; set; } = "MINUTES"; // MINUTES, HOURS, DAY

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
