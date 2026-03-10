// Nom.Data/Recipe/RecipeIngredientEntity.cs
using Nom.Data.Audit; // Assuming BaseEntity is in Nom.Data.Audit namespace
using Nom.Data.Reference; // For other references
using Nom.Data.Measurement; // For Measurement
using System.Collections.Generic; // Not strictly needed for this entity but often included

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a specific ingredient used in a recipe, including its quantity and measurement unit.
    /// Maps to the 'Recipe.recipe_ingredient' table.
    /// </summary>
    public class RecipeIngredientEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the associated RecipeEntity this ingredient belongs to.
        /// </summary>
        public long RecipeId { get; set; }

        /// <summary>
        /// Navigation property to the RecipeEntity.
        /// </summary>
        public virtual RecipeEntity Recipe { get; set; } = default!;

        /// <summary>
        /// Foreign key to the standardized IngredientEntity.
        /// </summary>
        public long IngredientId { get; set; }

        /// <summary>
        /// Navigation property to the standardized IngredientEntity.
        /// </summary>
        public virtual IngredientEntity Ingredient { get; set; } = default!;

        /// <summary>
        /// The quantity of the ingredient (e.g., 1.5, 0.5, 2.0).
        /// Corresponds to DECIMAL(18,4) NOT NULL.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Foreign key to the Measurement.Measurement table, indicating the unit of measurement
        /// for the quantity (e.g., "cup", "gram", "each").
        /// </summary>
        public long MeasurementId { get; set; }

        /// <summary>
        /// Navigation property to the associated MeasurementEntity.
        /// </summary>
        public virtual MeasurementEntity? Measurement { get; set; }

        /// <summary>
        /// The original raw text line of the ingredient as it appeared in the source recipe (e.g., "1 1/2 cups all-purpose flour").
        /// Useful for debugging, display, or if parsing is incomplete.
        /// </summary>
        public string RawLine { get; set; } = string.Empty;
    }
}
