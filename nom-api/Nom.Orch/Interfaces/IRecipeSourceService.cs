using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Stores the source a recipe was imported from on that recipe (<c>recipe.ScrapedDocument</c>),
    /// for the recipes imported before the staged import kept its record. A record is matched to a
    /// recipe by its normalized URL, else by its name and the batch's attribution, and only when
    /// the recipe's lines agree with the record's. Nothing is fetched from any site, every stored
    /// document is journaled, and the undo removes them again.
    /// </summary>
    public interface IRecipeSourceService
    {
        /// <summary>
        /// Matches every record of the batch and, with <paramref name="apply"/>, stores each one that
        /// matched one recipe. A recipe that already holds a different document of the record's
        /// format gets it as the next version only with <paramref name="newVersion"/>. Without
        /// <paramref name="apply"/> nothing is written and the result is what the same call would do.
        /// </summary>
        Task<RecipeSourceAttachResult> AttachAsync(RecipeSourceAttachRequestModel request, bool apply = false, bool newVersion = false);

        /// <summary>
        /// Removes the documents attached at or after <paramref name="sinceUtc"/> that are still
        /// there, and journals each removal. Without <paramref name="apply"/> nothing is written and
        /// the result is what the same call would do.
        /// </summary>
        Task<RecipeSourceUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false);
    }
}
