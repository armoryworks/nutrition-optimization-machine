namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe recommendation based on user behavior
    /// </summary>
    public class RecipeRecommendationModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string RecommendationType { get; set; } = string.Empty; // "similar", "trending", "popular", "recent", "seasonal"
        public decimal Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string> SimilarRecipes { get; set; } = new();
        public Dictionary<string, object>? UserBehaviorData { get; set; }
    }
} 