using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Builds a recipe's lines again from its stored source (<c>recipe.ScrapedDocument</c>) with the
    /// import's own line builder, in place, keeping the recipe's Id. Guards keep a person's edit, a
    /// planned recipe's foods and an approved recipe that would end worse. Every write is journaled in
    /// <c>recipe.RecipeReprocessJournal</c> with the rows before and after, and the undo restores them
    /// only where nothing changed since. Nothing is fetched from any site.
    /// </summary>
    public interface IRecipeReprocessService
    {
        /// <summary>
        /// Rebuilds the given recipes, or with <paramref name="orderOnly"/> only sets the order of
        /// their lines from the source and changes nothing else. Without <paramref name="apply"/>
        /// nothing is written and the result is what the same call would do.
        /// </summary>
        Task<RecipeReprocessResult> RebuildAsync(RecipeReprocessRequestModel request, bool apply = false, bool orderOnly = false);

        /// <summary>
        /// Soft-deletes the second copies of recipes imported twice (same URL, or same name and
        /// attribution, with lines that agree both ways), keeping the copy that is referenced,
        /// approved or older. Without <paramref name="apply"/> nothing is written.
        /// </summary>
        Task<RecipeDuplicateResult> RetireDuplicatesAsync(RecipeDuplicateRequestModel request, bool apply = false);

        /// <summary>
        /// Takes back the reprocess writes made at or after <paramref name="sinceUtc"/>, or of one
        /// batch, newest first, limited to one kind when given. A write whose recipe changed since is
        /// skipped. Without <paramref name="apply"/> nothing is written.
        /// </summary>
        Task<RecipeReprocessUndoResult> UndoAsync(DateTime? sinceUtc, string? batchId, string? kind = null, bool apply = false);
    }
}
