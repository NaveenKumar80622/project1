using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("system_app_lock")]
    public class SystemAppLock
    {
        [Key]
        [Column("id")]
        public int Id { get; set; } = 1;

        [Column("is_blocked")]
        public bool IsBlocked { get; set; } = false;

        [Column("title")]
        [MaxLength(255)]
        public string Title { get; set; } = "System Maintenance";

        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [Column("estimated_end_time")]
        public DateTime? EstimatedEndTime { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_by")]
        [MaxLength(64)]
        public string? UpdatedBy { get; set; }
    }
}
