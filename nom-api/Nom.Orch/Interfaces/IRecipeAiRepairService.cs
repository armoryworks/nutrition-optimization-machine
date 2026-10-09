using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>Outcome of one AI repair batch; <see cref="LastRecipeId"/> is the cursor for the next.</summary>
    public sealed record AiRepairBatchResult(
        int Examined,
        int StepsSplit,
        int QuantitiesFilled,
        int ProposalsRejected,
        int Cleared,
        int ModelCalls,
        long LastRecipeId);

    /// <summary>
    /// Repairs vetting-flagged (RequiresRevision) imports with the local model: splits a one-paragraph
    /// method into steps without rewording it, and fills ingredient quantities only where the amount is
    /// written in the recipe itself. Then re-vets; a clean recipe returns to NonCurated for the
    /// auto-approval rules. Each recipe gets one model attempt, recorded in the audit log with the
    /// original text, so every change is reversible.
    /// </summary>
    public interface IRecipeAiRepairService
    {
        /// <summary>Repairs candidates with Id greater than <paramref name="afterRecipeId"/>; Examined == 0 when done.</summary>
        Task<AiRepairBatchResult> RepairBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);
    }
}
