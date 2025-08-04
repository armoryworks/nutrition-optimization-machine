namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe suggestion analytics
    /// </summary>
    public class RecipeSuggestionAnalyticsModel
    {
        public int TotalSuggestions { get; set; }
        public int MatchedRecipes { get; set; }
        public int PartialMatches { get; set; }
        public decimal AverageMatchScore { get; set; }
        public List<string> TopCategories { get; set; } = new();
        public List<string> TopCuisines { get; set; } = new();
        public List<string> MostRequestedIngredients { get; set; } = new();
        public Dictionary<string, int> DifficultyDistribution { get; set; } = new();
        public Dictionary<string, decimal> CostDistribution { get; set; } = new();
        public List<string> PopularSubstitutions { get; set; } = new();
    }
} 