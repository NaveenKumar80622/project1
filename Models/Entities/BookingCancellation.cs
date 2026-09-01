using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PickNBook.Api.Models.Entities
{
    [Index(nameof(CashfreeRefundId), IsUnique = true)]
    public class BookingCancellation
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string BookingReference { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string BookingType { get; set; } = string.Empty; // "Flight", "Hotel", "Bus"

        public int PaymentId { get; set; }

        public string UserId { get; set; } = string.Empty;

        // Financial Breakdown Snapshot
        [Column(TypeName = "decimal(18,2)")]
        public decimal OriginalCustomerPaid { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal SupplierAmount { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal MarkupAmount { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal ConvenienceFee { get; set; }

        // Cancellation Results
        [Column(TypeName = "decimal(18,2)")]
        public decimal SupplierCancellationCharge { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal SupplierRefundAmount { get; set; }

        // Customer Refund Rules Breakdown
        [Column(TypeName = "decimal(18,2)")]
        public decimal MarkupRefunded { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal CouponForfeited { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal FeeRefunded { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CustomerRefundAmount { get; set; }

        // Trackers
        [MaxLength(100)]
        public string? SrdvChangeRequestId { get; set; } // Used for Async flight

        [MaxLength(50)]
        public string SrdvStatus { get; set; } = "Pending"; // e.g. Pending, Success, Failed

        [MaxLength(100)]
        public string? CashfreeRefundId { get; set; } // REF-CANCEL-{BookingId}-{CancellationId}

        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, Completed, Failed

        public string? FailureReason { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        
        public DateTime? CompletedAtUtc { get; set; }
    }
}
