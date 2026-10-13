using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>Outcome of one repair batch; <see cref="LastRecipeId"/> is the cursor for the next.</summary>
    public sealed record ScrapedRepairBatchResult(
        int Recipes,
        int RowsRepaired,
        int RowsStillUnparsed,
        int Revetted,
        int Cleared,
        long LastRecipeId);

    /// <summary>
    /// Rebuilds ingredient quantities on scraped recipes from each row's RawLine (deterministic
    /// parsing — no model involved) and re-runs vetting on recipes no human has reviewed, so
    /// imports that lost their quantities stop sitting in RequiresRevision for that reason alone.
    /// </summary>
    public interface IScrapedRecipeRepairService
    {
        /// <summary>Repairs scraped recipes with Id greater than <paramref name="afterRecipeId"/>; Recipes == 0 when done.</summary>
        Task<ScrapedRepairBatchResult> RepairBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);
    }
}
