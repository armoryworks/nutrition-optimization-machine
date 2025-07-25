using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class CreateRecipeRequest
    {
        [Required]
        public required string Name { get; set; }
        public string? Description { get; set; }
        // Additional fields from RecipeEntity as needed for creation
    }
}