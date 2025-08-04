namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for seasonal recipe suggestions
    /// </summary>
    public class SeasonalRecipeModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public List<string> SeasonalIngredients { get; set; } = new();
        public string SeasonalReason { get; set; } = string.Empty;
        public decimal SeasonalScore { get; set; }
    }
} 