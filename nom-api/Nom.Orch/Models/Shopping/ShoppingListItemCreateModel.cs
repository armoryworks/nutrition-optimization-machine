// File: Nom.Orch/Models/Shopping/ShoppingListItemCreateModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListItemCreateModel
    {
        public long ShoppingListId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Note { get; set; }
        public long? IngredientId { get; set; }
        public long? RecipeId { get; set; }
        public int? Position { get; set; }
    }
} 