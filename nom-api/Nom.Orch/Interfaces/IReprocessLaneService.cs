using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The reprocess lane on the lane frame (lane <c>reprocess</c>, tiers <c>rebuild</c> and
    /// <c>order</c>): it takes the detector's candidates, highest priority first, and rebuilds them
    /// through <see cref="IRecipeReprocessService"/>, writing only while its tier is live and only
    /// previewing otherwise. It records every decision, runs the rule checks A1 to A5 on every changed
    /// line, has the line judge (reader B) audit a sample, and hands the frame its figures and faults.
    /// </summary>
    public interface IReprocessLaneService
    {
        /// <summary>A hash of what decides a tier: the line rules, the rule checks and, for rebuilds, the line judge's prompt and model.</summary>
        string Signature(string tier);

        /// <summary>
        /// Takes at most <paramref name="max"/> candidates of the tier and rebuilds them; writes only
        /// when <paramref name="mode"/> is live. Live, it also takes the candidates decided in shadow.
        /// </summary>
        Task<ReprocessLaneBatchResult> RunBatchAsync(string tier, string mode, int max, CancellationToken cancellationToken = default);

        /// <summary>Audits a sample of the tier's decisions and evaluates the tier on the frame with the batch's faults.</summary>
        Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default);

        /// <summary>The tier's decisions in the last 24 hours, against its daily cap.</summary>
        Task<int> DecidedTodayAsync(string tier, CancellationToken cancellationToken = default);

        Task<ReprocessLaneStatusModel> StatusAsync(bool enabled, CancellationToken cancellationToken = default);
    }
}
