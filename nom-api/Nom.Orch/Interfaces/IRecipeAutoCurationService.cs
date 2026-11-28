using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>Outcome of one auto-approval batch; <see cref="LastRecipeId"/> is the cursor for the next.</summary>
    /// <summary><see cref="Refusals"/> counts the candidates the policy turned down, by rule; <see cref="Held"/> means the pause was on when the batch would have saved, so it kept nothing and the cursor stays.</summary>
    public sealed record AutoApproveBatchResult(int Examined, int Approved, long LastRecipeId, IReadOnlyDictionary<string, int>? Refusals = null, bool Held = false);

    /// <summary>
    /// A dry run of the auto-approval sweep over every candidate (plan item ck23, U7): how many the sweep examines, how many
    /// the policy or vetting refuse by rule, how many pass and still wait by the diet reason (not yet decided by the diet
    /// check, held as unknown by a severe diet type, not yet read by the second reader), how many it would approve now, and
    /// the first recipe ids of each reason. Nothing is written.
    /// </summary>
    public sealed record AutoApproveWaitingReport(int Candidates, int Refused, IReadOnlyDictionary<string, int> RefusedBy, int Passing, int Waiting,
        IReadOnlyDictionary<string, int> WaitingBy, int WouldApprove, IReadOnlyDictionary<string, IReadOnlyList<long>> Samples,
        int MaxUnquantifiedLines, bool RequireOwnImage, bool RequireDietUnderstood, bool RequireSecondReading);

    /// <summary>
    /// Approves recipes that meet <see cref="Nom.Orch.Services.Support.RecipeAutoApprovalPolicy"/> as the
    /// System person, recording an "auto-approved" curation note for each.
    /// </summary>
    public interface IRecipeAutoCurationService
    {
        /// <summary>Examines candidates with Id greater than <paramref name="afterRecipeId"/>; Examined == 0 when done.</summary>
        Task<AutoApproveBatchResult> AutoApproveBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default);

        /// <summary>The sweep's reasons for every candidate under the configured options, as it would decide them now, without writing (see <see cref="AutoApproveWaitingReport"/>); <paramref name="sample"/> recipe ids per reason.</summary>
        Task<AutoApproveWaitingReport> WaitingAsync(int sample = 20, CancellationToken cancellationToken = default);
    }
}
