using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>One detector batch: recipes read, how many a rebuild would change, by reason code, and the cursor for the next batch.</summary>
    public sealed record ReprocessDetectBatchResult(int Read, int Candidates, IReadOnlyDictionary<string, int> ByReason, long LastRecipeId);

    /// <summary>
    /// Finds the recipes a rebuild from their stored source would change (reprocess.md 2c): it
    /// reads every recipe with a source again with the current line rules, as a dry run of the
    /// rebuild, and stores the reasons in <c>recipe.ReprocessCandidate</c>. A recipe is read again
    /// only when the line rules' version or its newest source changes. Writes nothing to recipes.
    /// </summary>
    public interface IReprocessDetectorService
    {
        /// <summary>Reads the next recipes after <paramref name="afterRecipeId"/> that the current rules have not read yet; Read is 0 when the sweep is done.</summary>
        Task<ReprocessDetectBatchResult> DetectBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);

        /// <summary>
        /// The detector run by an operator over at most <paramref name="maxRecipes"/> recipes with a stored
        /// source after <paramref name="afterRecipeId"/>: counts by reason and tier, holds, and samples. It
        /// calls no model and waits on no pause switch. A dry run writes nothing and reads every such recipe,
        /// read before or not; with <paramref name="apply"/> it reads only those the current rules have not
        /// read and stores their reasons, as the lane's sweep does.
        /// </summary>
        Task<ReprocessDetectReport> DetectAsync(long afterRecipeId, int maxRecipes, bool apply, int sampleSize, CancellationToken cancellationToken = default);

        /// <summary>Marks every recipe the duplicate retire would retire (D8) and clears the mark elsewhere; returns how many are marked.</summary>
        Task<int> MarkDuplicatesAsync(CancellationToken cancellationToken = default);
    }
}
