using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_ip_rules")]
    public class SecurityIpRule
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("ip_address")]
        [MaxLength(45)]
        [Required]
        public string IpAddress { get; set; } = string.Empty;

        [Column("action")]
        [MaxLength(20)]
        [Required]
        public string Action { get; set; } = string.Empty; // WHITELIST, BLACKLIST, BLOCK

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = string.Empty; // ADMIN, USER, ADMIN_USER, B2B

        [Column("status")]
        [MaxLength(20)]
        [Required]
        public string Status { get; set; } = "ACTIVE"; // ACTIVE, WHITELISTED, BLACKLISTED, BLOCKED, EXPIRED, UNBLOCKED, INACTIVE

        [Column("source")]
        [MaxLength(20)]
        [Required]
        public string Source { get; set; } = "MANUAL"; // MANUAL, AUTOMATIC, SYSTEM

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
        [Required]
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        [Column("expiry_time")]
        public DateTime? ExpiryTime { get; set; }

        [Column("security_trigger")]
        [MaxLength(100)]
        public string? SecurityTrigger { get; set; }

        [Column("account_id")]
        public long? AccountId { get; set; }

        [Column("account_email")]
        [MaxLength(255)]
        public string? AccountEmail { get; set; }

        [Column("email_sent")]
        public bool EmailSent { get; set; } = false;

        [Column("email_status")]
        [MaxLength(20)]
        public string? EmailStatus { get; set; } // PENDING, SENT, FAILED, RETRY, CANCELLED

        [Column("created_by")]
        [MaxLength(100)]
        public string CreatedBy { get; set; } = "System";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
