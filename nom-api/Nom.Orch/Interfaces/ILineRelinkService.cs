using System;
using System.Collections.Generic;
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
        /// Moves the scraped lines of each given wording from the curated food they are on to the
        /// one named for them, for wordings a reviewer has judged ("tart apples" on a Granny Smith
        /// entry belongs on Apples). Without <paramref name="apply"/> nothing is written and
        /// Remaining is the number of lines that would move.
        /// </summary>
        Task<LineRelinkResult> MoveWordingsAsync(IReadOnlyCollection<LineRelinkWordingMove> moves, bool apply = false);

        /// <summary>
        /// Takes a duplicate food with no recipe lines left out of the catalog and leaves its name
        /// as an alias of the main entry alone, so a later line with that name lands there. With
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
