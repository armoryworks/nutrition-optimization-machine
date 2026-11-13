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
        /// need to tell apart from its main entry (well water and Water). Returns null when either
        /// food is not a live curated food or the target has no nutrition.
        /// </summary>
        Task<LineRelinkResult?> MoveFoodAsync(long fromIngredientId, long toIngredientId);

        /// <summary>Puts back every line moved at or after <paramref name="sinceUtc"/> that still sits where the move left it.</summary>
        Task<LineRelinkUndoResult> UndoAsync(DateTime sinceUtc);
    }
}
