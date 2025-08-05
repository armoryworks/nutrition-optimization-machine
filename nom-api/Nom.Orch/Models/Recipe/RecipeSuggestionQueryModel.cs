using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for recipe suggestion query parameters
    /// </summary>
    public class RecipeSuggestionQueryModel
    {
        public long? UserId { get; set; }
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
        public List<long>? IngredientIds { get; set; }
    }
} 