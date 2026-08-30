using System;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models
{
    public class Theme
    {
        [Key]
        public string Id { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string VariablesJson { get; set; } = "{}";
    }
}
