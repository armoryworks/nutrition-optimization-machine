namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe trending analysis
    /// </summary>
    public class RecipeTrendingModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string TrendingReason { get; set; } = string.Empty;
        public int ViewCount { get; set; }
        public int RatingCount { get; set; }
        public int CommentCount { get; set; }
        public decimal AverageRating { get; set; }
        public DateTime TrendingStartDate { get; set; }
        public List<string> TrendingFactors { get; set; } = new();
    }
} 