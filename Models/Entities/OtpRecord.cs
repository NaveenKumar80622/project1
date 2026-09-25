using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("otp_records")]
    public class OtpRecord
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("otp_id")]
        [MaxLength(64)]
        public string OtpId { get; set; } = Guid.NewGuid().ToString("N");

        [Column("identifier")]
        [MaxLength(100)]
        [Required]
        public string Identifier { get; set; } = string.Empty;

        [Column("identifier_type")]
        [MaxLength(10)]
        [Required]
        public string IdentifierType { get; set; } = "EMAIL";

        [Column("purpose")]
        [MaxLength(20)]
        [Required]
        public string Purpose { get; set; } = "REGISTRATION";

        [Column("otp_hash")]
        [MaxLength(128)]
        [Required]
        public string OtpHash { get; set; } = string.Empty;

        [Column("delivery_method")]
        [MaxLength(10)]
        [Required]
        public string DeliveryMethod { get; set; } = "EMAIL";

        [Column("attempt_count")]
        public int AttemptCount { get; set; } = 0;

        [Column("resend_count")]
        public int ResendCount { get; set; } = 0;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [Column("is_verified")]
        public bool IsVerified { get; set; } = false;
    }
}
