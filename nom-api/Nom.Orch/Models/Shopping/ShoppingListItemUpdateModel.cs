// File: Nom.Orch/Models/Shopping/ShoppingListItemUpdateModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListItemUpdateModel
    {
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public bool IsCompleted { get; set; }
        public string? Note { get; set; }
        public int? Position { get; set; }
    }
} 