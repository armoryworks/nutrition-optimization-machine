// File: Nom.Orch/Models/Shopping/ShoppingListRecipeAddModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListRecipeAddModel
    {
        public long ShoppingListId { get; set; }
        public long RecipeId { get; set; }
        public bool IncludeAllIngredients { get; set; } = true;
        public List<long>? SelectedIngredientIds { get; set; }
        public decimal? ScaleFactor { get; set; } = 1.0m;
    }
} 