using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    /// <summary>
    /// Model for smart shopping list generation request
    /// </summary>
    public class SmartShoppingListRequestModel
    {
        [Required]
        public long HouseholdId { get; set; }

        public List<long> RecipeIds { get; set; } = new();
        public List<long> MealPlanIds { get; set; } = new();
        public List<string> Preferences { get; set; } = new();
        public List<string> DietaryRestrictions { get; set; } = new();
        public int? ServingSize { get; set; }
        public bool IncludePantryItems { get; set; } = true;
        public bool OptimizeForBudget { get; set; } = false;
        public bool OptimizeForNutrition { get; set; } = false;
        public string? StorePreference { get; set; }
    }

    /// <summary>
    /// Model for AI-powered shopping list generation
    /// </summary>
    public class AIShoppingListRequestModel
    {
        [Required]
        public string Description { get; set; } = string.Empty;

        public List<string> Ingredients { get; set; } = new();
        public List<string> Meals { get; set; } = new();
        public List<string> Preferences { get; set; } = new();
        public List<string> DietaryRestrictions { get; set; } = new();
        public int? ServingSize { get; set; }
        public int? DaysToPlan { get; set; }
        public decimal? BudgetLimit { get; set; }
        public string? StorePreference { get; set; }
        public bool IncludePantryItems { get; set; } = true;
        public bool OptimizeForBudget { get; set; } = false;
        public bool OptimizeForNutrition { get; set; } = false;
    }

    /// <summary>
    /// Model for smart shopping list response
    /// </summary>
    public class SmartShoppingListResponseModel
    {
        public long ShoppingListId { get; set; }
        public string ShoppingListName { get; set; } = string.Empty;
        public List<SmartShoppingListItemModel> Items { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public decimal EstimatedTotal { get; set; }
        public int TotalItems { get; set; }
        public string GenerationMethod { get; set; } = string.Empty;
        public List<string> Recommendations { get; set; } = new();
        public List<string> Substitutions { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Model for smart shopping list item
    /// </summary>
    public class SmartShoppingListItemModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public decimal? EstimatedPrice { get; set; }
        public string? Brand { get; set; }
        public string? Store { get; set; }
        public bool IsPantryItem { get; set; }
        public bool IsSubstitution { get; set; }
        public string? OriginalItem { get; set; }
        public List<string> RecipeSources { get; set; } = new();
        public List<string> NutritionalInfo { get; set; } = new();
        public int Priority { get; set; } = 1; // 1 = High, 2 = Medium, 3 = Low
    }

    /// <summary>
    /// Model for shopping list optimization
    /// </summary>
    public class ShoppingListOptimizationModel
    {
        public long ShoppingListId { get; set; }
        public bool OptimizeForBudget { get; set; } = false;
        public bool OptimizeForNutrition { get; set; } = false;
        public bool OptimizeForTime { get; set; } = false;
        public decimal? BudgetLimit { get; set; }
        public List<string> StorePreferences { get; set; } = new();
        public List<string> DietaryRestrictions { get; set; } = new();
        public List<string> ExcludedItems { get; set; } = new();
    }

    /// <summary>
    /// Model for shopping list suggestions
    /// </summary>
    public class ShoppingListSuggestionModel
    {
        public string Type { get; set; } = string.Empty; // "substitution", "addition", "removal", "combination"
        public string Description { get; set; } = string.Empty;
        public decimal? CostSavings { get; set; }
        public string? NutritionalBenefit { get; set; }
        public string? TimeBenefit { get; set; }
        public List<string> Items { get; set; } = new();
        public int Confidence { get; set; } // 1-100
    }

    /// <summary>
    /// Model for AI shopping list generation response
    /// </summary>
    public class AIShoppingListResponseModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public SmartShoppingListResponseModel? ShoppingList { get; set; }
        public List<ShoppingListSuggestionModel> Suggestions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string? AIReasoning { get; set; }
    }

    /// <summary>
    /// Model for shopping list analytics
    /// </summary>
    public class ShoppingListAnalyticsModel
    {
        public long ShoppingListId { get; set; }
        public decimal TotalCost { get; set; }
        public decimal AverageItemCost { get; set; }
        public int TotalItems { get; set; }
        public int CompletedItems { get; set; }
        public decimal CompletionRate { get; set; }
        public List<string> Categories { get; set; } = new();
        public Dictionary<string, int> CategoryBreakdown { get; set; } = new();
        public List<string> MostExpensiveItems { get; set; } = new();
        public List<string> MostPurchasedItems { get; set; } = new();
        public decimal BudgetUtilization { get; set; }
        public string? NutritionalScore { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Model for shopping list template
    /// </summary>
    public class ShoppingListTemplateModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<SmartShoppingListItemModel> DefaultItems { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public bool IsPublic { get; set; }
        public long CreatedByUserId { get; set; }
        public DateTime CreatedDate { get; set; }
        public int UsageCount { get; set; }
    }

    /// <summary>
    /// Model for shopping list generation history
    /// </summary>
    public class ShoppingListGenerationHistoryModel
    {
        public long Id { get; set; }
        public long ShoppingListId { get; set; }
        public string GenerationMethod { get; set; } = string.Empty;
        public string RequestData { get; set; } = string.Empty;
        public string ResponseData { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime GeneratedDate { get; set; }
        public long GeneratedByUserId { get; set; }
        public decimal ProcessingTime { get; set; }
    }
} 