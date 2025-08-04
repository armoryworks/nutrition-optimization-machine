namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe similarity analysis
    /// </summary>
    public class RecipeSimilarityModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public decimal SimilarityScore { get; set; }
        public List<string> CommonIngredients { get; set; } = new();
        public List<string> CommonCategories { get; set; } = new();
        public List<string> CommonTags { get; set; } = new();
        public string SimilarityReason { get; set; } = string.Empty;
    }
} 