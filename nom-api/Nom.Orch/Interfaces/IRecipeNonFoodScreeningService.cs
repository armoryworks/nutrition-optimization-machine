using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>Outcome of one non-food screening batch; <see cref="LastRecipeId"/> is the cursor for the next.</summary>
    public sealed record NonFoodBatchResult(
        int Scanned,
        int Screened,
        int Rejected,
        int Unsure,
        int ModelCalls,
        long LastRecipeId);

    /// <summary>
    /// Rejects unreviewed public-domain recipes that are not food at all (cleaning, toiletry and remedy
    /// receipts from old household cookbooks): only when <see cref="Nom.Orch.Services.Support.NonFoodRecipeFilter"/>
    /// flags the recipe AND the local model says it is not food. Unsure answers leave it alone. Each
    /// candidate is screened once per screening version, recorded in the audit log.
    /// </summary>
    public interface IRecipeNonFoodScreeningService
    {
        /// <summary>Screens recipes with Id greater than <paramref name="afterRecipeId"/>; Scanned == 0 when done.</summary>
        Task<NonFoodBatchResult> ScreenBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);
    }
}
