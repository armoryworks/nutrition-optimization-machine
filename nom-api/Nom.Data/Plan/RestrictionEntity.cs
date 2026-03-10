using System;
using Nom.Data.Person; // For PersonEntity
using Nom.Data.Reference; // For RestrictionType
using Nom.Data.Recipe; // For IngredientEntity
using Nom.Data.Nutrient; // For NutrientEntity

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a dietary or other restriction within a plan.
    /// Maps to the 'Plan.restriction' table.
    /// </summary>
    public class RestrictionEntity : BaseEntity
    {
        // Changed to nullable: A restriction can exist without being directly tied to a plan
        // if it's purely person-specific. The CHECK constraint will enforce at least one of PlanId or PersonId.
        public long? PlanId { get; set; }
        public virtual PlanEntity? Plan { get; set; } // Also make navigation property nullable

        public long? PersonId { get; set; } // NULLable if restriction applies to all plan participants, or just a specific person on the plan
        public virtual PersonEntity? Person { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public long? RestrictionTypeId { get; set; } // NULLable in SQL, FK to ReferenceEntity
        public virtual ReferenceEntity? RestrictionType { get; set; } // e.g., Allergy, Preference, Medical

        public long? IngredientId { get; set; } // NULLable in SQL
        public virtual IngredientEntity? Ingredient { get; set; } // If restriction is about a specific ingredient

        public long? NutrientId { get; set; } // NULLable in SQL
        public virtual NutrientEntity? Nutrient { get; set; } // If restriction is about a specific nutrient (e.g., "Low Sodium")

        /// <summary>
        /// Severity level (1-5 scale). 1 = mild preference, 5 = absolute (e.g., allergy).
        /// Affects filtering and ranking behavior in meal planning.
        /// </summary>
        public int? Severity { get; set; } = 3;

        public DateOnly? BeginDate { get; set; }

        public DateOnly? EndDate { get; set; }
    }
}
