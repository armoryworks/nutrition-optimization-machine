namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Response model for recipe scraping
    /// </summary>
    public class RecipeScrapingResponseModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
} 