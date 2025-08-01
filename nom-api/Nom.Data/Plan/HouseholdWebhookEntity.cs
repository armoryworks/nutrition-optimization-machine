// File: Nom.Data/Plan/HouseholdWebhookEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    [Table("HouseholdWebhook", Schema = "plan")]
    public class HouseholdWebhookEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(2047)]
        public string Url { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? EventType { get; set; }

        public bool IsActive { get; set; } = true;
    }
}