namespace Nom.Orch.Models.Shopping
{
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
        public int UsageCount { get; set; }
    }
}