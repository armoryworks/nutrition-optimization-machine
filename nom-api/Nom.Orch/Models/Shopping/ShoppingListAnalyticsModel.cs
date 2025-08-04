namespace Nom.Orch.Models.Shopping
{
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
} 