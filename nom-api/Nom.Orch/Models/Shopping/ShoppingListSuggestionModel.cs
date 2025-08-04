namespace Nom.Orch.Models.Shopping
{
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
} 