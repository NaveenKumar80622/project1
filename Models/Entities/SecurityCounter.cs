using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_counters")]
    public class SecurityCounter
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("counter_key")]
        [MaxLength(50)]
        [Required]
        public string CounterKey { get; set; } = string.Empty;

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = string.Empty;

        [Column("dimension_type")]
        [MaxLength(50)]
        [Required]
        public string DimensionType { get; set; } = string.Empty; // IP, ACCOUNT, EMAIL, IP_ACCOUNT, IP_EMAIL, API_IP

        [Column("dimension_value")]
        [MaxLength(255)]
        [Required]
        public string DimensionValue { get; set; } = string.Empty;

        [Column("current_count")]
        public int CurrentCount { get; set; } = 0;

        [Column("limit_value")]
        public int LimitValue { get; set; }

        [Column("period_start")]
        public DateTime PeriodStart { get; set; } = DateTime.UtcNow;

        [Column("period_end")]
        public DateTime PeriodEnd { get; set; }

        [Column("last_attempt_at")]
        public DateTime? LastAttemptAt { get; set; } = DateTime.UtcNow;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
