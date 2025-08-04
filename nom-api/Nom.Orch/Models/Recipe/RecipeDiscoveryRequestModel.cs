namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe discovery request
    /// </summary>
    public class RecipeDiscoveryRequestModel
    {
        public List<string>? Ingredients { get; set; }
        public List<string>? ExcludedIngredients { get; set; }
        public List<string>? Cuisines { get; set; }
        public List<string>? DietaryRestrictions { get; set; }
        public List<string>? MealTypes { get; set; }
        public int? MaxPrepTime { get; set; }
        public int? MaxCookTime { get; set; }
        public string? Difficulty { get; set; }
        public decimal? MaxCost { get; set; }
        public bool IncludeSeasonalRecipes { get; set; } = true;
        public bool IncludeTrendingRecipes { get; set; } = true;
        public bool IncludePersonalizedRecipes { get; set; } = true;
        public int Limit { get; set; } = 20;
    }
} 