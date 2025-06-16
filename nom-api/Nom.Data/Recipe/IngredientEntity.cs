using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Recipe
{
    [Table("Ingredient", Schema = "recipe")] // Adjusted: Table name capitalized, schema lowercase
    public class IngredientEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        // Navigation property to IngredientNutrientEntity
        public virtual ICollection<IngredientNutrientEntity>? Nutrients { get; set; }

        // Navigation property back to RecipeIngredientEntity
        public virtual ICollection<RecipeIngredientEntity>? UsedInRecipes { get; set; }
    }
}