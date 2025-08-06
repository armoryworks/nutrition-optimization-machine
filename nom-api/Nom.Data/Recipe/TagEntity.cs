using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a tag that can be applied to recipes.
    /// Maps to the 'Recipe.tag' table.
    /// </summary>
    [Table("Tag", Schema = "recipe")]
    public class TagEntity : BaseEntity
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public string? Color { get; set; } // Hex color code
        public string? Icon { get; set; } // Icon identifier

        public long CurationStatusId { get; set; } = 9000L; // Default to NonCurated

        // Navigation properties
        public virtual ICollection<RecipeTagEntity> RecipeTags { get; set; } = new List<RecipeTagEntity>();
    }
} 