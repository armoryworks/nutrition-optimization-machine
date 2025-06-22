using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
    [Table("Restriction", Schema = "plan")] // Table name capitalized, schema lowercase
    public class RestrictionEntity : BaseEntity
    {
        // Changed to nullable: A restriction can exist without being directly tied to a plan
        // if it's purely person-specific. The CHECK constraint will enforce at least one of PlanId or PersonId.
        public long? PlanId { get; set; }
        [ForeignKey(nameof(PlanId))]
        public virtual PlanEntity? Plan { get; set; } // Also make navigation property nullable

        public long? PersonId { get; set; } // NULLable if restriction applies to all plan participants, or just a specific person on the plan
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity? Person { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        public long? RestrictionTypeId { get; set; } // NULLable in SQL, FK to ReferenceEntity
        [ForeignKey(nameof(RestrictionTypeId))]
        public virtual ReferenceEntity? RestrictionType { get; set; } // e.g., Allergy, Preference, Medical

        public long? IngredientId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity? Ingredient { get; set; } // If restriction is about a specific ingredient

        public long? NutrientId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity? Nutrient { get; set; } // If restriction is about a specific nutrient (e.g., "Low Sodium")

        [Column(TypeName = "date")]
        public DateOnly? BeginDate { get; set; }

        [Column(TypeName = "date")]
        public DateOnly? EndDate { get; set; }
    }
}
