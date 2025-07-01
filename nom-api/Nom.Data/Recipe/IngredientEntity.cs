// Nom.Data/Recipe/IngredientEntity.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Nutrient;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a standardized ingredient in the system (e.g., "All-Purpose Flour").
    /// This is the canonical representation of an ingredient, independent of how it's measured or used in a recipe.
    /// Maps to the 'Recipe.ingredient' table.
    /// </summary>
    [Table("Ingredient", Schema = "recipe")]
    public class IngredientEntity : BaseEntity
    {
        /// <summary>
        /// The standardized name of the ingredient (e.g., "Chicken Breast", "All-Purpose Flour").
        /// This should be unique and consistently cased.
        /// Corresponds to VARCHAR(255) NOT NULL.
        /// </summary>
        [Required]
        [MaxLength(1023)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A brief description of the ingredient.
        /// Corresponds to VARCHAR(1023) NULLABLE.
        /// </summary>
        [MaxLength(2047)]
        public string? Description { get; set; }

        /// <summary>
        /// The FoodData Central (FDC) ID for this ingredient, if it originated from FDC data.
        /// Useful for traceability and linking back to the FDC database.
        /// Corresponds to VARCHAR(50) NULLABLE.
        /// </summary>
        [MaxLength(50)] // FDC IDs are typically strings like "170110", "170557"
        public string? FdcId { get; set; }

        // Navigation properties
        public virtual ICollection<RecipeIngredientEntity> RecipeIngredients { get; set; } = new List<RecipeIngredientEntity>();
        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();
        public virtual ICollection<IngredientAliasEntity> Aliases { get; set; } = new List<IngredientAliasEntity>(); // New collection for aliases
    }
}
