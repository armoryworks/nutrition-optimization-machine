using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin revert of the quantities the AI repair lane filled as a count in pieces from a quote that
    /// states no count ("a glassful of good red wine", "a clean tin"). Each journaled fill is checked
    /// again with <see cref="Services.Support.RecipeRepairGrounding.PieceRefusal"/>; a fill that fails
    /// gets back the quantity and measurement its journal entry recorded; "a" or "an" before one
    /// countable item ("a bay leaf") is a count and stays. The reasons to revert can be chosen one by
    /// one, and the result counts every reason either way. A dry run unless asked to
    /// apply, every change written to the audit log, and a batch can be undone from it. A recipe's
    /// curation status is never changed; the result counts the approved recipes that would stop
    /// passing the line rules.
    /// </summary>
    public interface IAiRepairRevertService
    {
        Task<QuantityRevertResult> RevertAsync(bool apply = false, int maxLines = 5000, int sampleSize = 50, IReadOnlyCollection<string>? reasons = null);

        /// <summary>
        /// Gives back the filled quantity to every line reverted at or after <paramref name="sinceUtc"/>
        /// that still holds what the revert gave it.
        /// </summary>
        Task<LineCorrectionUndoResult> UndoAsync(DateTime sinceUtc);
    }
}
