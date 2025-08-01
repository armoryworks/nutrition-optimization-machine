// File: Nom.Data/Plan/HouseholdEventNotifierEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    [Table("HouseholdEventNotifier", Schema = "plan")]
    public class HouseholdEventNotifierEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? EventType { get; set; }

        [MaxLength(255)]
        public string? NotificationType { get; set; }

        [Column(TypeName = "text")]
        public string? Configuration { get; set; }

        public bool IsActive { get; set; } = true;
    }
}