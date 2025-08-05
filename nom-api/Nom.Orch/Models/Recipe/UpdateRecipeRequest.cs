// File: Nom.Orch/Models/Recipe/UpdateRecipeRequest.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class UpdateRecipeRequest
    {
        [Required]
        public long Id { get; set; }

        [Required]
        [MaxLength(511)]
        public required string Name { get; set; }

        [MaxLength(2047)]
        public string? Description { get; set; }

        public List<RecipeIngredientModel> Ingredients { get; set; } = new List<RecipeIngredientModel>();
        public List<RecipeStepModel> Steps { get; set; } = new List<RecipeStepModel>();
    }
}