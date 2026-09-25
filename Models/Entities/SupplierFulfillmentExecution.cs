using System;
using System.ComponentModel.DataAnnotations;
using PickNBook.Api.Models.Payments;

namespace PickNBook.Api.Models.Entities
{
    public class SupplierFulfillmentExecution
    {
        [Key]
        public int Id { get; set; }

        public int PaymentId { get; set; }
        public Payment? Payment { get; set; }

        [MaxLength(50)]
        public string BookingType { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? SupplierReference { get; set; }

        [MaxLength(50)]
        public string SupplierBookingStatus { get; set; } = string.Empty;

        public string? SupplierResponseJson { get; set; }

        public int? ReservationId { get; set; }

        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
