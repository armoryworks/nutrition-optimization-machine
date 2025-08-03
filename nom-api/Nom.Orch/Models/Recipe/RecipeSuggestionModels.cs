using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe suggestion query parameters
    /// </summary>
    public class RecipeSuggestionQueryModel
    {
        public int Limit { get; set; } = 10;
        public int MaxMissingIngredients { get; set; } = 5;
        public int MaxMissingTools { get; set; } = 5;
        public bool IncludeIngredientsOnHand { get; set; } = true;
        public bool IncludeToolsOnHand { get; set; } = true;
        public string? QueryFilter { get; set; }
        public List<string>? Categories { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? DietaryRestrictions { get; set; }
        public int? MaxPrepTime { get; set; }
        public int? MaxCookTime { get; set; }
        public decimal? MaxDifficulty { get; set; }
        public List<string>? Cuisines { get; set; }
        public bool IncludePublicRecipes { get; set; } = true;
        public bool IncludePrivateRecipes { get; set; } = false;
    }

    /// <summary>
    /// Model for AI-powered recipe suggestion request
    /// </summary>
    public class AIRecipeSuggestionRequestModel
    {
        [Required]
        public string Description { get; set; } = string.Empty;

        public List<string> AvailableIngredients { get; set; } = new();
        public List<string> AvailableTools { get; set; } = new();
        public List<string> Preferences { get; set; } = new();
        public List<string> DietaryRestrictions { get; set; } = new();
        public List<string> DislikedIngredients { get; set; } = new();
        public int? ServingSize { get; set; }
        public int? MaxPrepTime { get; set; }
        public int? MaxCookTime { get; set; }
        public decimal? BudgetLimit { get; set; }
        public string? Cuisine { get; set; }
        public string? MealType { get; set; } // breakfast, lunch, dinner, snack
        public string? Difficulty { get; set; } // easy, medium, hard
        public bool IncludeNutritionalInfo { get; set; } = true;
        public bool IncludeSubstitutions { get; set; } = true;
    }

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

    /// <summary>
    /// Model for recipe suggestion response
    /// </summary>
    public class RecipeSuggestionResponseModel
    {
        public List<RecipeSuggestionResponseItemModel> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public string SuggestionMethod { get; set; } = string.Empty;
        public List<string> Recommendations { get; set; } = new();
        public Dictionary<string, object>? Analytics { get; set; }
    }

    /// <summary>
    /// Model for AI recipe suggestion response
    /// </summary>
    public class AIRecipeSuggestionResponseModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<RecipeSuggestionResponseItemModel> Suggestions { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public List<string> Substitutions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string? AIReasoning { get; set; }
        public Dictionary<string, object>? NutritionalAnalysis { get; set; }
        public decimal? EstimatedTotalCost { get; set; }
    }

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

    /// <summary>
    /// Model for recipe suggestion analytics
    /// </summary>
    public class RecipeSuggestionAnalyticsModel
    {
        public int TotalSuggestions { get; set; }
        public int MatchedRecipes { get; set; }
        public int PartialMatches { get; set; }
        public decimal AverageMatchScore { get; set; }
        public List<string> TopCategories { get; set; } = new();
        public List<string> TopCuisines { get; set; } = new();
        public List<string> MostRequestedIngredients { get; set; } = new();
        public Dictionary<string, int> DifficultyDistribution { get; set; } = new();
        public Dictionary<string, decimal> CostDistribution { get; set; } = new();
        public List<string> PopularSubstitutions { get; set; } = new();
    }
} 