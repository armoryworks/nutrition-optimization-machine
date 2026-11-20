using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin re-check of the scraped recipes the prose-rewrite lane has already taken out of the
    /// source-prose quarantine, against the recipe's stored source with the lane's own check. A
    /// recipe that fails goes back into quarantine so the lane rewrites it again; its curation
    /// status is left as it is. Deterministic text rules only, every recipe put back is written
    /// to the audit log, and it can be taken out again from there.
    /// </summary>
    public interface IProseRecheckService
    {
        /// <summary>
        /// Checks every cleared recipe and, with <paramref name="apply"/>, puts up to
        /// <paramref name="maxRecipes"/> failing ones back into quarantine and clears their rewrite
        /// stamp. A recipe with a step a person has edited is left alone. Without
        /// <paramref name="apply"/> nothing is written and the result is what the same call would do.
        /// </summary>
        Task<ProseRecheckResult> RecheckAsync(bool apply = false, int maxRecipes = 50000, int sampleSize = 50);

        /// <summary>
        /// Takes the recipes put back into quarantine at or after <paramref name="sinceUtc"/> out of
        /// it again, unless the rewrite lane has worked on them since.
        /// </summary>
        Task<ProseRecheckUndoResult> UndoAsync(DateTime sinceUtc);
    }
}
