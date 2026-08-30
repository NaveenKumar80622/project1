using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_account_locks")]
    public class SecurityAccountLock
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("account_id")]
        public long AccountId { get; set; }

        [Column("account_email")]
        [MaxLength(255)]
        [Required]
        public string AccountEmail { get; set; } = string.Empty;

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = string.Empty;

        [Column("lock_status")]
        [MaxLength(50)]
        [Required]
        public string LockStatus { get; set; } = string.Empty; // TEMPORARILY_LOCKED, PERMANENTLY_LOCKED, SUSPENDED

        [Column("lock_reason")]
        [MaxLength(500)]
        public string? LockReason { get; set; }

        [Column("security_trigger")]
        [MaxLength(100)]
        public string? SecurityTrigger { get; set; }

        [Column("locked_at")]
        public DateTime LockedAt { get; set; } = DateTime.UtcNow;

        [Column("expires_at")]
        public DateTime? ExpiresAt { get; set; }

        [Column("unlocked_at")]
        public DateTime? UnlockedAt { get; set; }

        [Column("unlocked_by")]
        [MaxLength(100)]
        public string? UnlockedBy { get; set; }

        [Column("ip_address")]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
