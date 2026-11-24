using System;
using System.Collections.Generic;
using Nom.Data.Shopping;
using Nom.Orch.Models.Recipe;

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

        /// <summary>
        /// The item's note to serve: NOM's recipe note with the recipe's current name, or, for a recipe that was removed
        /// (<paramref name="removed"/>), its title, the plain note that it is no longer available and its source link; otherwise
        /// the stored note.
        /// </summary>
        public static string? Shown(ShoppingListItemEntity item, IReadOnlyDictionary<long, RemovedRecipeModel>? removed = null)
        {
            if (item.RecipeId is { } id && item.Recipe == null && removed != null && removed.TryGetValue(id, out var gone)
                && (item.Note == null || item.Note.StartsWith(RecipePrefix, StringComparison.Ordinal)))
                return $"{ForRecipe(gone.Title)}. {RecipeRemovalService.RemovedNote} Source: {gone.SourceUrl}";
            return item.RecipeId != null && item.Recipe != null && item.Note != null && item.Note.StartsWith(RecipePrefix, StringComparison.Ordinal)
                ? ForRecipe(item.Recipe.Name)
                : item.Note;
        }
    }
}
