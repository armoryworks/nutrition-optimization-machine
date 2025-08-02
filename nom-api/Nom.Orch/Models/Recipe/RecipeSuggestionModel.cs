using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe suggestions based on various criteria
    /// </summary>
    public class RecipeSuggestionModel
    {
        public string? Query { get; set; }
        public List<long>? FoodIds { get; set; }
        public List<long>? ToolIds { get; set; }
        public int Limit { get; set; } = 10;
        public bool IncludeIngredients { get; set; } = false;
        public bool IncludeSteps { get; set; } = false;
    }
} 