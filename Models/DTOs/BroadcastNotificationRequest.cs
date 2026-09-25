using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs
{
    public class BroadcastNotificationRequest
    {
        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = "Broadcast";

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = "System";

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Severity { get; set; } = "Info";

        [MaxLength(100)]
        public string? ReferenceType { get; set; }

        [MaxLength(100)]
        public string? ReferenceId { get; set; }

        [MaxLength(500)]
        public string? ActionUrl { get; set; }

        [MaxLength(50)]
        public string TargetRole { get; set; } = "All"; // "All", "Customer", "Admin", "SuperAdmin"

        [MaxLength(150)]
        public string? IdempotencyKey { get; set; }
    }
}
