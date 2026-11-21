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
        long LastRecipeId,
        int StepsSplit = 0,
        int TextDecoded = 0,
        int RecipesHeld = 0,
        int RecipesRebuilt = 0,
        int RowsReread = 0);

    /// <summary>
    /// Rebuilds ingredient quantities on scraped recipes from each row's RawLine, divides a
    /// one-paragraph method at its sentence ends (deterministic parsing — no model involved) and
    /// re-runs vetting on recipes no human has reviewed, so imports that lost their quantities stop
    /// sitting in RequiresRevision for that reason alone.
    /// </summary>
    public interface IScrapedRecipeRepairService
    {
        /// <summary>Repairs scraped recipes with Id greater than <paramref name="afterRecipeId"/>; Recipes == 0 when done.</summary>
        Task<ScrapedRepairBatchResult> RepairBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);

        /// <summary>Gives back the amount each line had before a start-up repair at or after <paramref name="sinceUtc"/> changed it; a dry run unless <paramref name="apply"/> is set.</summary>
        Task<Nom.Orch.Models.Curation.StartupFillUndoResult> UndoFillsAsync(System.DateTime sinceUtc, bool apply = false);
    }
}
