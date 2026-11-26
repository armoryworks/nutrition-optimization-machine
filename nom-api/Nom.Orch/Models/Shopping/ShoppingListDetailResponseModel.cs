using System.Collections.Generic;

namespace Nom.Orch.Models.Shopping
{
    /// <summary>
    /// A shopping list with its items. An item added from a recipe that was removed (taken from a forbidden source) carries
    /// the recipe's title, the plain note that it is no longer available and its source link in its note
    /// (<see cref="Nom.Orch.Services.Support.ShoppingItemNote.Shown"/>), as the smart list and the grocery export serve it.
    /// </summary>
    public class ShoppingListDetailResponseModel : ShoppingListResponseModel
    {
        public List<ShoppingListItemResponseModel> Items { get; set; } = new();
    }
}
