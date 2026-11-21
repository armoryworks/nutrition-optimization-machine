using System;
using Nom.Data.Shopping;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The note NOM writes on a shopping-list item added from a recipe ("From recipe: ..."). It is served with the
    /// recipe's name as it is now, not as it was when the item was added, so a title the title rule later reduced
    /// (a facts-only source's distinctive title) is never served from a stored copy. A note a person wrote is served as written.
    /// </summary>
    public static class ShoppingItemNote
    {
        public const string RecipePrefix = "From recipe: ";

        public static string ForRecipe(string recipeName) => RecipePrefix + recipeName;

        /// <summary>The item's note to serve: NOM's recipe note with the recipe's current name, or the stored note.</summary>
        public static string? Shown(ShoppingListItemEntity item) =>
            item.RecipeId != null && item.Recipe != null && item.Note != null && item.Note.StartsWith(RecipePrefix, StringComparison.Ordinal)
                ? ForRecipe(item.Recipe.Name)
                : item.Note;
    }
}
