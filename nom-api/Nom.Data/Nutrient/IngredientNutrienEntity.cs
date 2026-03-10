// Nom.Data/Nutrient/IngredientNutrientEntity.cs
using Nom.Data.Recipe; // Required for Ingredient navigation property
using Nom.Data.Measurement; // Required for Measurement navigation property

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents the nutritional content of a specific ingredient (e.g., protein in chicken breast).
    /// This entity links an Ingredient to a Nutrient and specifies the amount per typical serving/100g.
    /// Maps to the 'Nutrient.ingredient_nutrient' table.
    /// </summary>
    public class IngredientNutrientEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Recipe.Ingredient table, identifying the ingredient.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long IngredientId { get; set; }

        /// <summary>
        /// Navigation property to the associated IngredientEntity.
        /// </summary>
        public virtual IngredientEntity Ingredient { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Nutrient.Nutrient table, identifying the nutrient.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long NutrientId { get; set; }

        /// <summary>
        /// Navigation property to the associated NutrientEntity.
        /// </summary>
        public virtual NutrientEntity Nutrient { get; set; } = default!;

        /// <summary>
        /// The amount of the nutrient present in the ingredient (per 100g by default, or per serving).
        /// Corresponds to DECIMAL NOT NULL.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Foreign key to the Measurement.Measurement table, indicating the unit of measurement
        /// for the amount (e.g., "mg", "g", "kcal").
        /// </summary>
        public long MeasurementId { get; set; }

        /// <summary>
        /// Navigation property to the associated MeasurementEntity.
        /// </summary>
        public virtual MeasurementEntity? Measurement { get; set; } // Nullable if 'unknown' is allowed for type 0

        /// <summary>
        /// The FoodData Central (FDC) ID for the specific nutrient, if this data originated from FDC.
        /// Useful for traceability and debugging. Nullable if not from FDC.
        /// </summary>
        public string? FdcId { get; set; }
    }
}
