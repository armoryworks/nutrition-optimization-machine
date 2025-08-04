using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
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
} 