using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_api_rules")]
    public class SecurityApiRule
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("api_name")]
        [MaxLength(100)]
        [Required]
        public string ApiName { get; set; } = string.Empty;

        [Column("http_method")]
        [MaxLength(10)]
        [Required]
        public string HttpMethod { get; set; } = string.Empty;

        [Column("url_pattern")]
        [MaxLength(255)]
        [Required]
        public string UrlPattern { get; set; } = string.Empty;

        [Column("scope")]
        [MaxLength(20)]
        [Required]
        public string Scope { get; set; } = string.Empty;

        [Column("rate_limit_value")]
        public int? RateLimitValue { get; set; } = 100;

        [Column("rate_limit_period")]
        [MaxLength(20)]
        public string? RateLimitPeriod { get; set; } = "PER_MINUTE"; // PER_MINUTE, PER_HOUR, PER_DAY

        [Column("is_blockable")]
        public bool IsBlockable { get; set; } = true;

        [Column("is_public")]
        public bool IsPublic { get; set; } = false;

        [Column("is_exception")]
        public bool IsException { get; set; } = false;

        [Column("status")]
        [MaxLength(20)]
        public string Status { get; set; } = "ACTIVE";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
