using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models
{
    public class ThemeConfig
    {
        [Key]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string ValueJson { get; set; } = "{}";
    }
}
