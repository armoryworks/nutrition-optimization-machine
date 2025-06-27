// Nom.Data/Recipe/RecipeIngredientEntity.cs
using Nom.Data.Reference; // For MeasurementType
using Nom.Data.Nutrient; // For IngredientNutrientEntity
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations; // For MaxLength

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a specific ingredient used in a recipe, including its quantity and measurement unit.
    /// Maps to the 'Recipe.recipe_ingredient' table.
    /// </summary>
    [Table("RecipeIngredient", Schema = "recipe")]
    public class RecipeIngredientEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the associated RecipeEntity this ingredient belongs to.
        /// </summary>
        public long RecipeId { get; set; }

        /// <summary>
        /// Navigation property to the RecipeEntity.
        /// </summary>
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity Recipe { get; set; } = default!;

        /// <summary>
        /// Foreign key to the standardized IngredientEntity.
        /// </summary>
        public long IngredientId { get; set; }

        /// <summary>
        /// Navigation property to the standardized IngredientEntity.
        /// </summary>
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity Ingredient { get; set; } = default!;

        /// <summary>
        /// The quantity of the ingredient (e.g., 1.5, 0.5, 2.0).
        /// Corresponds to DECIMAL(18,4) NOT NULL.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// Foreign key to the Reference.Reference table, indicating the unit of measurement
        /// for the quantity (e.g., "cup", "gram", "each").
        /// </summary>
        public long MeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated MeasurementType ReferenceEntity.
        /// </summary>
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity? MeasurementType { get; set; } // It can be nullable if "unknown" is allowed

        /// <summary>
        /// The original raw text line of the ingredient as it appeared in the source recipe (e.g., "1 1/2 cups all-purpose flour").
        /// Useful for debugging, display, or if parsing is incomplete.
        /// </summary>
        [Required]
        [MaxLength(500)] // Adjust max length as needed
        public string RawLine { get; set; } = string.Empty;
    }
}
