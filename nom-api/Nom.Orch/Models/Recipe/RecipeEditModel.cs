// File: Nom.Orch/Models/Recipe/RecipeEditModel.cs

using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeEditModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<RecipeIngredientModel> Ingredients { get; set; } = new List<RecipeIngredientModel>();
        public List<RecipeStepModel> Steps { get; set; } = new List<RecipeStepModel>();
    }
} 