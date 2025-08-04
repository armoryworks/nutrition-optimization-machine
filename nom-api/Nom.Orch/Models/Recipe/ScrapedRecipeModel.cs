namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Scraped recipe data model
    /// </summary>
    public class ScrapedRecipeModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Image { get; set; }
        public string? SourceUrl { get; set; }
        public string? SourceSite { get; set; }
        public string? PrepTime { get; set; }
        public string? CookTime { get; set; }
        public string? TotalTime { get; set; }
        public string? RecipeYield { get; set; }
        public decimal? RecipeYieldQuantity { get; set; }
        public decimal? RecipeServings { get; set; }
        public List<ScrapedIngredientModel> Ingredients { get; set; } = new();
        public List<ScrapedStepModel> Steps { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<string> Categories { get; set; } = new();
    }
} 