using System;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Deterministic cleanup of the step markers scraped methods carry inside their steps ("1. Heat",
    /// "Step 2: Mix", "2In a bowl", "hours.2.Stir", "Toss to mix. 3."): the markers are taken out of
    /// the step text and nothing else changes; a step that held only a marker is removed. Steps a
    /// person has edited are left alone. Every step changed is journaled with its whole old text,
    /// and the undo puts it back.
    /// </summary>
    public interface IProseMarkerService
    {
        /// <summary>
        /// Strips the markers from the steps of up to <paramref name="maxRecipes"/> scraped recipes.
        /// Without <paramref name="apply"/> nothing is written and the result is what the same call
        /// would do.
        /// </summary>
        Task<ProseMarkerStripResult> StripAsync(bool apply = false, int maxRecipes = 1000, int sampleSize = 20, CancellationToken cancellationToken = default);

        /// <summary>
        /// Puts back the step text stripped at or after <paramref name="sinceUtc"/>, unless the step
        /// has been changed since; each step put back is journaled, so a repeat finds nothing left.
        /// Without <paramref name="apply"/> nothing is written and the result is what the same call
        /// would do.
        /// </summary>
        Task<ProseMarkerUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false, CancellationToken cancellationToken = default);
    }
}
