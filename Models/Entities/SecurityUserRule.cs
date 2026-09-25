using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_user_rules")]
    public class SecurityUserRule
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("user_id")]
        [MaxLength(100)]
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Column("rule_type")]
        [MaxLength(20)]
        [Required]
        public string RuleType { get; set; } = "USER"; // "USER" or "URL"

        [Column("route")]
        [MaxLength(255)]
        public string? Route { get; set; } // e.g. "/api/v1/hotels/book" (NO HTTP Method)

        [Column("action")]
        [MaxLength(20)]
        [Required]
        public string Action { get; set; } = "BLOCK"; // BLOCK, BLACKLIST, WHITELIST

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = "USER"; // USER, ADMIN

        [Column("status")]
        [MaxLength(20)]
        [Required]
        public string Status { get; set; } = "ACTIVE"; // ACTIVE, EXPIRED, UNBLOCKED

        [Column("source")]
        [MaxLength(20)]
        [Required]
        public string Source { get; set; } = "MANUAL"; // MANUAL, AUTOMATIC

        [Column("reason")]
        [MaxLength(500)]
        public string? Reason { get; set; }

        [Column("block_type")]
        [MaxLength(20)]
        [Required]
        public string BlockType { get; set; } = "TEMPORARY"; // TEMPORARY, PERMANENT

        [Column("duration_minutes")]
        public int? DurationMinutes { get; set; }

        [Column("start_time")]
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        [Column("expiry_time")]
        public DateTime? ExpiryTime { get; set; }

        [Column("created_by")]
        public string? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
