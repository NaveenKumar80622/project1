using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("busblockedseatprices")]
    public class BusBlockedSeatPrice
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string TraceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string SeatName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseFare { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GstAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PublishedFare { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MarkupAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [MaxLength(50)]
        public string? CouponCode { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GrandTotal { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
