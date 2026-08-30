using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PickNBook.Api.Models
{
    public class SecurityRule
    {
        [Key]
        public int Id { get; set; }

        public string RuleCategory { get; set; } = string.Empty; // e.g. LOGIN_SECURITY, DAILY_LOGIN, PROGRESSIVE_LOCKOUT

        public string RuleJson { get; set; } = string.Empty; // Stores the flexible payload for the specific category

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
