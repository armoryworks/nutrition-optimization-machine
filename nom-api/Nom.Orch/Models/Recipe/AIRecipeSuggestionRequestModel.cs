using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
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
} 