using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Response model for recipe suggestions
    /// </summary>
    public class RecipeSuggestionResponseModel
    {
        public List<RecipeSuggestionResultModel> Suggestions { get; set; } = new List<RecipeSuggestionResultModel>();
        public int TotalCount { get; set; }
    }
} 