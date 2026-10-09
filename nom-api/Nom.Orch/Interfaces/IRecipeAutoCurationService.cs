using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>Outcome of one auto-approval batch; <see cref="LastRecipeId"/> is the cursor for the next.</summary>
    public sealed record AutoApproveBatchResult(int Examined, int Approved, long LastRecipeId);

    /// <summary>
    /// Approves recipes that meet <see cref="Nom.Orch.Services.Support.RecipeAutoApprovalPolicy"/> as the
    /// System person, recording an "auto-approved" curation note for each.
    /// </summary>
    public interface IRecipeAutoCurationService
    {
        /// <summary>Examines candidates with Id greater than <paramref name="afterRecipeId"/>; Examined == 0 when done.</summary>
        Task<AutoApproveBatchResult> AutoApproveBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);
    }
}
