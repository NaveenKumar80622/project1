using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models
{
    [Table("flight_search_logs")]
    public class FlightSearchLog
    {
        [Key]
        public int Id { get; set; }

        public DateTime SearchedAtUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(80)]
        public string FromCity { get; set; } = string.Empty;

        [Required]
        [MaxLength(80)]
        public string ToCity { get; set; } = string.Empty;

        public DateOnly? DepartDate { get; set; }
        public DateOnly? ReturnDate { get; set; }

        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }

        [Required]
        [MaxLength(20)]
        public string TripType { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? UserId { get; set; }

        public bool IsGuest { get; set; }

        [MaxLength(80)]
        public string? UserOrGuestId { get; set; }

        // New SRDV Fields
        [MaxLength(255)]
        public string? TraceId { get; set; }

        [MaxLength(50)]
        public string? EndUserIp { get; set; }
    }
}
