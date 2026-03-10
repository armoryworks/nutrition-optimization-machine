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
}
