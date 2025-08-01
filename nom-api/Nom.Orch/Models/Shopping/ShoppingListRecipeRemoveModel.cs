// File: Nom.Orch/Models/Shopping/ShoppingListRecipeRemoveModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListRecipeRemoveModel
    {
        public long ShoppingListId { get; set; }
        public long RecipeId { get; set; }
        public bool RemoveAllIngredients { get; set; } = true;
        public List<long>? SelectedIngredientIds { get; set; }
    }
} 