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
        public List<long> PlanIds { get; set; } = new();
        public List<string> Preferences { get; set; } = new();
        public List<string> DietaryRestrictions { get; set; } = new();
        public int? ServingSize { get; set; }
        public bool IncludePantryItems { get; set; } = true;
        public bool OptimizeForBudget { get; set; } = false;
        public bool OptimizeForNutrition { get; set; } = false;
        public string? StorePreference { get; set; }
    }
} 