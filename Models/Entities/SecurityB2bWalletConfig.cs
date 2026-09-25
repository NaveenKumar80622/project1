using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    [Table("security_b2b_wallet_config")]
    public class SecurityB2bWalletConfig
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("min_wallet_amount")]
        public decimal MinWalletAmount { get; set; } = 10000.00m;

        [Column("auto_unblock_enabled")]
        public bool AutoUnblockEnabled { get; set; } = false;

        [Column("applies_to_temp_blocks")]
        public bool AppliesToTempBlocks { get; set; } = true;

        [Column("applies_to_auto_blocks")]
        public bool AppliesToAutoBlocks { get; set; } = true;

        [Column("applies_to_rate_limit")]
        public bool AppliesToRateLimit { get; set; } = false;

        [Column("email_on_restriction")]
        public bool EmailOnRestriction { get; set; } = true;

        [Column("email_template_restriction_id")]
        public long? EmailTemplateRestrictionId { get; set; }

        [Column("email_on_unblock")]
        public bool EmailOnUnblock { get; set; } = true;

        [Column("email_template_unblock_id")]
        public long? EmailTemplateUnblockId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
