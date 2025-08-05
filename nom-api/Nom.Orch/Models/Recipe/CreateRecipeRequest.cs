using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class CreateRecipeRequest
    {
        [Required]
        public required string Name { get; set; }
        public string? Description { get; set; }
        public List<RecipeIngredientModel> Ingredients { get; set; } = new List<RecipeIngredientModel>();
        public List<RecipeStepModel> Steps { get; set; } = new List<RecipeStepModel>();
    }
}