// File: Nom.Orch/Models/Recipe/RecipeCreateModel.cs

using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeCreateModel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<RecipeIngredientModel> Ingredients { get; set; } = new List<RecipeIngredientModel>();
        public List<RecipeStepModel> Steps { get; set; } = new List<RecipeStepModel>();
    }
} 