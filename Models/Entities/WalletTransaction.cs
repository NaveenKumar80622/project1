using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("wallet_transactions")]
    public class WalletTransaction
    {
        [Key]
        public long Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        [MaxLength(20)]
        public string TransactionType { get; set; } = string.Empty; // "Credit", "Debit", "Refund"

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal RunningBalance { get; set; }

        [Required]
        [MaxLength(50)]
        public string ReferenceType { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string RefCode { get; set; } = string.Empty;

        [MaxLength(300)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Completed"; // "Completed", "Failed", "Pending"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
