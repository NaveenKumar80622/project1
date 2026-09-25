using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_audit_logs")]
    public class SecurityAuditLog
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("event_type")]
        [MaxLength(50)]
        [Required]
        public string EventType { get; set; } = string.Empty;

        [Column("user_or_admin_id")]
        [MaxLength(64)]
        public string? UserOrAdminId { get; set; }

        [Column("scope")]
        [MaxLength(20)]
        public string Scope { get; set; } = "SYSTEM";

        [Column("account_id")]
        public long? AccountId { get; set; }

        [Column("account_email")]
        [MaxLength(255)]
        public string? AccountEmail { get; set; }

        [Column("email")]
        [MaxLength(100)]
        public string? Email { get; set; }

        [Column("ip_address")]
        [MaxLength(45)]
        [Required]
        public string IpAddress { get; set; } = string.Empty;

        [Column("session_id")]
        [MaxLength(64)]
        public string? SessionId { get; set; }

        [Column("action")]
        [MaxLength(100)]
        [Required]
        public string Action { get; set; } = string.Empty;

        [Column("status")]
        [MaxLength(20)]
        [Required]
        public string Status { get; set; } = string.Empty; // 'SUCCESS' | 'FAILED' | 'BLOCKED'

        [Column("reason_details")]
        public string? ReasonDetails { get; set; }

        [Column("previous_value", TypeName = "longtext")]
        public string? PreviousValue { get; set; }

        [Column("new_value", TypeName = "longtext")]
        public string? NewValue { get; set; }



        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
