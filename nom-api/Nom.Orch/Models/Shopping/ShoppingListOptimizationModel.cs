namespace Nom.Orch.Models.Shopping
{
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
} 