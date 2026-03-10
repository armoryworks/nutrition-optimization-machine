using Nom.Data.Reference; // For TimeframeType
using Nom.Data.Measurement; // For Measurement
using Nom.Data.Recipe; // For IngredientEntity
using Nom.Data.Nutrient; // For NutrientEntity

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a quantifiable or specific item/target within a larger goal.
    /// Maps to the 'Plan.goal_item' table.
    /// </summary>
    public class GoalItemEntity : BaseEntity
    {
        public long GoalId { get; set; }
        public virtual GoalEntity Goal { get; set; } = default!;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        // BIT NOT NULL DEFAULT(0) in SQL maps to bool in C#
        public bool IsQuantifiable { get; set; }

        public long? IngredientId { get; set; } // NULLable in SQL
        public virtual IngredientEntity? Ingredient { get; set; }

        public long? NutrientId { get; set; } // NULLable in SQL
        public virtual NutrientEntity? Nutrient { get; set; }

        public long? TimeframeTypeId { get; set; } // NULLable in SQL
        public virtual ReferenceEntity? TimeframeType { get; set; } // e.g., Daily, Weekly, Monthly

        public long? MeasurementId { get; set; } // NULLable in SQL
        public virtual MeasurementEntity? Measurement { get; set; } // e.g., grams, calories, count

        public decimal? MeasurementMinimum { get; set; } // DECIMAL NULL in SQL

        public decimal? MeasurementMaximum { get; set; } // DECIMAL NULL in SQL
    }
}