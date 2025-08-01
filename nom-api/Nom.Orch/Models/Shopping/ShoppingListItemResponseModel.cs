// File: Nom.Orch/Models/Shopping/ShoppingListItemResponseModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListItemResponseModel
    {
        public long Id { get; set; }
        public long ShoppingListId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public bool IsCompleted { get; set; }
        public string? Note { get; set; }
        public long? IngredientId { get; set; }
        public long? RecipeId { get; set; }
        public int? Position { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
} 