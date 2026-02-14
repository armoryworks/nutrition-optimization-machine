namespace Nom.Orch.Models.Recipe
{
    public class RecipeDashboardAnalyticsModel
    {
        public int TotalRecipes { get; set; }
        public Dictionary<string, int> RecipesByStatus { get; set; } = new();
        public List<RecipeSummaryModel> TopRatedRecipes { get; set; } = new();
        public List<RecipeSummaryModel> RecentlyCreated { get; set; } = new();
        public List<IngredientUsageModel> MostUsedIngredients { get; set; } = new();
    }

    public class RecipeSummaryModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Rating { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class IngredientUsageModel
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int UsageCount { get; set; }
    }
}
