using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin repair of scraped recipe lines that sit on a curated food other than the one their
    /// own wording names ("1 cup water" on the well-water entry belongs on Water). Deterministic
    /// name rules only, every move is written to the audit log, and a move can be undone from it.
    /// </summary>
    public interface ILineRelinkService
    {
        Task<LineRelinkPreview> PreviewAsync(int sampleSize = 50);
        Task<LineRelinkResult> ApplyAsync(int maxLines = 5000);

        /// <summary>
        /// Moves every recipe line of one curated food to another, for a food the catalog does not
        /// need to tell apart from its main entry (well water and Water). A line that contains one
        /// of <paramref name="keepWords"/> stays ("delicious" keeps "red delicious apples" on its
        /// variety while "red apples" moves). Returns null when either food is not a live curated
        /// food or the target has no nutrition.
        /// </summary>
        Task<LineRelinkResult?> MoveFoodAsync(long fromIngredientId, long toIngredientId, string? keepWords = null);

        /// <summary>
        /// Takes a duplicate food with no recipe lines left out of the catalog and leaves its name
        /// as an alias of the main entry, so a later line with that name lands there. With
        /// <paramref name="takeName"/> the main entry takes the retired food's name and its own
        /// former name becomes the alias. Nothing is retired while a recipe line or a household's
        /// data still points at the food. Returns null when either food is not a live curated food
        /// or the main entry has no nutrition.
        /// </summary>
        Task<FoodRetireResult?> RetireAsync(long fromIngredientId, long toIngredientId, bool takeName = false);

        /// <summary>
        /// Puts back every line moved at or after <paramref name="sinceUtc"/> that still sits where
        /// the move left it, and brings back every food retired since then.
        /// </summary>
        Task<LineRelinkUndoResult> UndoAsync(DateTime sinceUtc);
    }
}
