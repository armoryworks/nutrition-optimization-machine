using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For GoalType

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a specific goal within a plan (e.g., "Lose 5kg", "Eat more protein").
    /// Maps to the 'Plan.goal' table.
    /// </summary>
    [Table("Goal", Schema = "plan")] // Table name capitalized, schema lowercase
    public class GoalEntity : BaseEntity
    {
        [Required]
        public long PlanId { get; set; }
        [ForeignKey(nameof(PlanId))]
        public virtual PlanEntity Plan { get; set; } = default!;

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(2047)]
        public string Description { get; set; } = string.Empty;

        public long? GoalTypeId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(GoalTypeId))]
        public virtual ReferenceEntity? GoalType { get; set; }

        [Column(TypeName = "date")]
        public DateOnly? BeginDate { get; set; }

        [Column(TypeName = "date")]
        public DateOnly? EndDate { get; set; }

        // Navigation property for goal items
        public virtual ICollection<GoalItemEntity>? GoalItems { get; set; }
    }
}