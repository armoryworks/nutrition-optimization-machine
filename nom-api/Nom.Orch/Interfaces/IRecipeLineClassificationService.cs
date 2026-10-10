using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>Outcome of one line classification batch; <see cref="LastRecipeId"/> is the cursor for the next.</summary>
    public sealed record LineClassifyBatchResult(
        int Examined,
        int Deterministic,
        int ModelClassified,
        int Unclassified,
        int Cleared,
        int ModelCalls,
        long LastRecipeId);

    /// <summary>
    /// Classifies unquantified ingredient lines of unreviewed recipes (RequiresRevision, NonCurated) as a real
    /// ingredient with a missing amount, or as something that never had one: a reference to another
    /// preparation, equipment, a note, method text or a merge artefact. Obvious cases are classified
    /// deterministically, the rest by the local model; then the recipe is re-vetted. Never changes a
    /// quantity or a raw line. Each recipe gets one attempt per classifier version, recorded in the audit log.
    /// </summary>
    public interface IRecipeLineClassificationService
    {
        /// <summary>Classifies candidates with Id greater than <paramref name="afterRecipeId"/>; Examined == 0 when done.</summary>
        Task<LineClassifyBatchResult> ClassifyBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);
    }
}
