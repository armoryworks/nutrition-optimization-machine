namespace Nom.Orch.Models.Shopping
{
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
} 