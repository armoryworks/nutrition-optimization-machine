namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for AI recipe suggestion response
    /// </summary>
    public class AIRecipeSuggestionResponseModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<RecipeSuggestionResponseItemModel> Suggestions { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public List<string> Substitutions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string? AIReasoning { get; set; }
        public Dictionary<string, object>? NutritionalAnalysis { get; set; }
        public decimal? EstimatedTotalCost { get; set; }
    }
} 