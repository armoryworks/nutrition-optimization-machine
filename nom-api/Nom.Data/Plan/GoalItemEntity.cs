using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For TimeframeType, MeasurementType
using Nom.Data.Recipe; // For IngredientEntity
using Nom.Data.Nutrient; // For NutrientEntity

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a quantifiable or specific item/target within a larger goal.
    /// Maps to the 'Plan.goal_item' table.
    /// </summary>
    [Table("GoalItem", Schema = "plan")] // Table name capitalized, schema lowercase
    public class GoalItemEntity : BaseEntity
    {
        [Required]
        public long GoalId { get; set; }
        [ForeignKey(nameof(GoalId))]
        public virtual GoalEntity Goal { get; set; } = default!;

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(2047)]
        public string Description { get; set; } = string.Empty;

        [Required]
        // BIT NOT NULL DEFAULT(0) in SQL maps to bool in C#
        public bool IsQuantifiable { get; set; }

        public long? IngredientId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity? Ingredient { get; set; }

        public long? NutrientId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity? Nutrient { get; set; }

        public long? TimeframeTypeId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(TimeframeTypeId))]
        public virtual ReferenceEntity? TimeframeType { get; set; } // e.g., Daily, Weekly, Monthly

        public long? MeasurementTypeId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity? MeasurementType { get; set; } // e.g., grams, calories, count

        [Column(TypeName = "decimal(18,2)")] // Ensure proper decimal mapping
        public decimal? MeasurementMinimum { get; set; } // DECIMAL NULL in SQL

        [Column(TypeName = "decimal(18,2)")] // Ensure proper decimal mapping
        public decimal? MeasurementMaximum { get; set; } // DECIMAL NULL in SQL
    }
}