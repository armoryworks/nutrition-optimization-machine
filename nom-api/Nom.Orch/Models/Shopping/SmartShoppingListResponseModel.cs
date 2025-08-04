namespace Nom.Orch.Models.Shopping
{
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
} 