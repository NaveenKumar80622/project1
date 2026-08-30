using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("ip_access_rules")]
    public class IpAccessRule
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("ip_address")]
        [MaxLength(45)]
        [Required]
        public string IpAddress { get; set; } = string.Empty;

        [Column("list_type")]
        [MaxLength(10)]
        [Required]
        public string ListType { get; set; } = "BLACKLIST";

        [Column("status")]
        [MaxLength(10)]
        [Required]
        public string Status { get; set; } = "ACTIVE";

        [Column("is_permanent")]
        public bool IsPermanent { get; set; } = true;

        [Column("reason")]
        public string? Reason { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("blocked_at")]
        public DateTime? BlockedAt { get; set; }

        [Column("expires_at")]
        public DateTime? ExpiresAt { get; set; }

        [Column("created_by")]
        [MaxLength(64)]
        public string? CreatedBy { get; set; }
    }
}
