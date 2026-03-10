using System;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a category that can be applied to recipes.
    /// Maps to the 'Recipe.category' table.
    /// </summary>
    public class CategoryEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Color { get; set; } // Hex color code
        public string? Icon { get; set; } // Icon identifier

        public long CurationStatusId { get; set; } = 9000L; // Default to NonCurated

        // Navigation properties
        public virtual ICollection<RecipeCategoryEntity> RecipeCategories { get; set; } = new List<RecipeCategoryEntity>();
    }
}
