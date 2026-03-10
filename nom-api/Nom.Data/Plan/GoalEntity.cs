using System;
using System.Collections.Generic;
using Nom.Data.Reference; // For GoalType

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a specific goal within a plan (e.g., "Lose 5kg", "Eat more protein").
    /// Maps to the 'Plan.goal' table.
    /// </summary>
    public class GoalEntity : BaseEntity
    {
        public long PlanId { get; set; }
        public virtual PlanEntity Plan { get; set; } = default!;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long? GoalTypeId { get; set; } // NULLable in SQL
        public virtual ReferenceEntity? GoalType { get; set; }

        public DateOnly? BeginDate { get; set; }

        public DateOnly? EndDate { get; set; }

        // Navigation property for goal items
        public virtual ICollection<GoalItemEntity>? GoalItems { get; set; }
    }
}