using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("notification_recipients")]
    public class NotificationRecipient
    {
        [Key]
        public int Id { get; set; }

        public int NotificationId { get; set; }

        [ForeignKey(nameof(NotificationId))]
        public InAppNotification? Notification { get; set; }

        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [MaxLength(50)]
        public string? RecipientRole { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
