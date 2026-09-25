using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("hotel_blocked_prices")]
    public class HotelBlockedPrice
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string ResultIndex { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string HotelCode { get; set; } = string.Empty;

        public string TraceId { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal OfferedPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Tax { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MarkupAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GrandTotal { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
