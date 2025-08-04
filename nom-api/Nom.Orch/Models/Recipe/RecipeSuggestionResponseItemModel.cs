namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe suggestion response item
    /// </summary>
    public class RecipeSuggestionResponseItemModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public decimal Rating { get; set; }
        public int RatingCount { get; set; }
        public string? PrepTime { get; set; }
        public string? CookTime { get; set; }
        public string? TotalTime { get; set; }
        public int Servings { get; set; }
        public string Difficulty { get; set; } = string.Empty;
        public string Cuisine { get; set; } = string.Empty;
        public List<string> Categories { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<string> MissingIngredients { get; set; } = new();
        public List<string> MissingTools { get; set; } = new();
        public decimal MatchScore { get; set; }
        public string MatchReason { get; set; } = string.Empty;
        public List<string> Substitutions { get; set; } = new();
        public Dictionary<string, object>? NutritionalInfo { get; set; }
        public decimal? EstimatedCost { get; set; }
        public bool IsPublic { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
} 