using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The deterministic wording tiers as a lane on the lane frame (lane <c>wording-tiers</c>, one
    /// frame tier per wording tier). Each pass takes in-use uncurated wordings that have no row in the
    /// verified wording table, decides them by <see cref="WordingTiers"/> and the link gate, records
    /// every decision, and writes the rows of a tier only while the frame holds that tier live,
    /// through the verified wording loader with an evidence key and a batch-scoped relink, so one
    /// retract takes any row back. A shadow or paused tier runs the loader as a dry run.
    /// </summary>
    public interface IWordingTierLaneService
    {
        /// <summary>A hash of what decides a tier: the tier rules, the gate, the alcohol rules, the head-default thresholds, and the auditor's and second check's models and prompt.</summary>
        string Signature(string tier);

        /// <summary>
        /// Decides at most <paramref name="max"/> wordings. <paramref name="modes"/> gives each tier's
        /// mode (a tier missing is shadow); <paramref name="remaining"/> the writes each tier may still
        /// make in 24 hours (missing is no cap). A wording whose tier has no allowance left is left for
        /// a later pass.
        /// </summary>
        Task<WordingTierBatchResult> RunBatchAsync(IReadOnlyDictionary<string, string> modes, int max, IReadOnlyDictionary<string, int>? remaining = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// An operator's dry run over at most <paramref name="max"/> waiting wordings after the first
        /// <paramref name="skip"/>, most lines first, decided or not: coverage per tier, holds, the
        /// loader's refusals, abstentions and up to <paramref name="sampleSize"/> samples per tier. Calls
        /// no model, records no decision and writes nothing.
        /// </summary>
        Task<WordingTierPreview> PreviewAsync(int skip, int max, int sampleSize, CancellationToken cancellationToken = default);

        /// <summary>Audits a sample of the tier's decisions and evaluates the tier on the frame with its faults.</summary>
        Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default);

        /// <summary>The tier's decisions that write in the last 24 hours, against its daily cap.</summary>
        Task<int> WritesTodayAsync(string tier, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The two model readings the wording tier lane's audit uses: the auditor (the roster's auditor,
    /// a third family) reads a sampled pair, and a flag counts as an error only when the second check
    /// (reader B, re-reading the pair blind) also finds it wrong.
    /// </summary>
    public interface IWordingTierJudges
    {
        bool IsConfigured { get; }
        string AuditorModel { get; }
        string CheckModel { get; }
        ILaneAuditor Auditor { get; }
        ILaneAuditCheck Check { get; }
    }
}
