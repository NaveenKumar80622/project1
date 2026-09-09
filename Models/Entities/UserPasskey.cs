using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("UserPasskeys")]
    public class UserPasskey
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        [Column(TypeName = "varbinary(255)")]
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();

        [Required]
        [Column(TypeName = "longblob")]
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        [Column(TypeName = "varbinary(64)")]
        public byte[]? UserHandle { get; set; }

        [Column(TypeName = "int unsigned")]
        public uint SignatureCounter { get; set; }

        [Required]
        [MaxLength(50)]
        public string CredType { get; set; } = "public-key";

        public Guid? AaGuid { get; set; }

        [MaxLength(150)]
        public string? DeviceName { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? LastUsedAtUtc { get; set; }
    }
}
