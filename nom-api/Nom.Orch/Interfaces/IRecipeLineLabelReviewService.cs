using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin correction of recipe lines already labelled as something other than an ingredient
    /// (a reference, equipment, a note, method text, an artefact) where <see cref="Services.Support.RecipeLineKindGuard"/>
    /// says the line is an ingredient. Deterministic rules only, a dry run unless asked to apply,
    /// every change written to the audit log, and a batch can be undone from it. A recipe's
    /// curation status is never changed; the result counts the approved recipes that would stop
    /// passing the line rules so the owner can decide what happens to them.
    /// </summary>
    public interface IRecipeLineLabelReviewService
    {
        /// <summary>
        /// Reports both rules; with <paramref name="apply"/> returns up to <paramref name="maxLines"/>
        /// lines of the certain cases to ingredient, or of the wider rule too when
        /// <paramref name="shortLinesOnCuratedFoods"/> is set.
        /// </summary>
        Task<LineLabelReviewResult> ReviewAsync(bool apply = false, bool shortLinesOnCuratedFoods = false, int maxLines = 5000, int sampleSize = 50);

        /// <summary>
        /// Gives back its earlier label to every line corrected at or after <paramref name="sinceUtc"/>
        /// that is still labelled as an ingredient.
        /// Without <paramref name="apply"/> nothing is written and the result is what the same call would do.
        /// </summary>
        Task<LineCorrectionUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false);
    }
}
