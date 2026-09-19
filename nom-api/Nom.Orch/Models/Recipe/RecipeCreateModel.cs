// File: Nom.Orch/Models/Recipe/RecipeCreateModel.cs

using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeCreateModel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>How many servings the recipe yields; per-serving nutrition divides by this.</summary>
        public long? Servings { get; set; }
        /// <summary>Size of one serving, e.g. 250 (with <see cref="ServingQuantityMeasurementId"/> = grams).</summary>
        public decimal? ServingQuantity { get; set; }
        public long? ServingQuantityMeasurementId { get; set; }
        public List<RecipeIngredientModel> Ingredients { get; set; } = new List<RecipeIngredientModel>();
        public List<RecipeStepModel> Steps { get; set; } = new List<RecipeStepModel>();
    }
} 