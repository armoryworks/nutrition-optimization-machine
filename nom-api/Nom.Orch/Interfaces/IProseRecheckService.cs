using System;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Re-check of the scraped recipes whose prose is shown, against the recipe's stored source with
    /// the prose check (<c>ProseCheck</c>, versioned) and the current stock-phrase index. A recipe
    /// that fails has its prose withheld: it stays listed, usable and approved as it was, its
    /// description and steps are never served, and the rewrite lane takes it up. Deterministic text
    /// rules only, every recipe withheld is written to the audit log, and it can be released again
    /// from there.
    /// </summary>
    public interface IProseRecheckService
    {
        /// <summary>
        /// Checks every shown scraped recipe and, with <paramref name="apply"/>, withholds the prose
        /// of up to <paramref name="maxRecipes"/> failing ones, approved first, in transactions of
        /// 500, and stamps the passing ones with the check version. A recipe with a step a person has
        /// edited is left alone and listed. An apply is refused while no stock-phrase index has been
        /// built. An apply that leaves no failure shown is recorded as a full run. Without
        /// <paramref name="apply"/> nothing is written and the result is what the same call would do.
        /// </summary>
        Task<ProseRecheckResult> RecheckAsync(bool apply = false, int maxRecipes = 500, int sampleSize = 50, CancellationToken cancellationToken = default);

        /// <summary>The last full run recorded by <see cref="RecheckAsync"/>; null when there has been none.</summary>
        Task<ProseRecheckRun?> LastRunAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Why a full run is due at <paramref name="now"/>, or null when none is: never while no
        /// stock-phrase index has been built; otherwise when no full run is recorded, when the prose
        /// check or the stock-phrase index has a version the last full run did not use, or when
        /// <paramref name="interval"/> has passed since it.
        /// </summary>
        Task<string?> DueAsync(TimeSpan interval, DateTime now, CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases the recipes withheld at or after <paramref name="sinceUtc"/>, with the check
        /// version they had, unless their prose has been replaced since (by the rewrite lane or by a
        /// person); each release is journaled, so a repeat finds nothing left. Also takes recipes an
        /// earlier re-check put back into quarantine out of it again with their earlier rewrite stamp,
        /// unless the lane has cleared them with a rewrite since. Without <paramref name="apply"/>
        /// nothing is written and the result is what the same call would do.
        /// </summary>
        Task<ProseRecheckUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false);
    }
}
