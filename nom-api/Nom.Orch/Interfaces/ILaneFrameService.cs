using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Data.Curation;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The shared frame every writing lane runs in: its mode (shadow, live or paused) kept in
    /// curation.LaneState, its decisions in curation.LaneDecision, the audit of a stratified sample
    /// of them, and the evaluation that qualifies, pauses and resumes it by Wilson bounds. A pause
    /// undoes the lane's writes since its last clean window through its <see cref="ILaneUndo"/>.
    /// See docs/architecture/lane-frame.md.
    /// </summary>
    public interface ILaneFrameService
    {
        /// <summary>The mode a tier writes in now: paused when the whole lane is paused, else the tier's own mode; shadow when it has no row.</summary>
        Task<string> ModeAsync(string lane, string tier = LaneTiers.Whole, CancellationToken cancellationToken = default);

        /// <summary>Records one decision and returns its id.</summary>
        Task<long> RecordAsync(LaneDecisionInput decision, CancellationToken cancellationToken = default);

        /// <summary>
        /// Audits a stratified random sample of the tier's unaudited decisions that write since
        /// <paramref name="sinceUtc"/>. A decision the auditor flags is an error only when
        /// <paramref name="check"/> agrees.
        /// </summary>
        Task<LaneAuditPassResult> AuditAsync(string lane, string tier, DateTime sinceUtc, LaneSamplePlan plan, ILaneAuditor auditor, ILaneAuditCheck check, Random? random = null, CancellationToken cancellationToken = default);

        /// <summary>Evaluates a tier, changes its mode when a bar is crossed, journals the change and, on a pause, undoes its writes since its last clean window.</summary>
        Task<LaneQualifyVerdict> EvaluateAsync(LaneEvaluateRequest request, CancellationToken cancellationToken = default);

        /// <summary>Every lane and tier with a row, with its figures.</summary>
        Task<IReadOnlyList<LaneFrameStatusModel>> StatusAsync(CancellationToken cancellationToken = default);

        /// <summary>The tier's undo of its writes since its last clean window; null when the tier has no row or the lane no undo.</summary>
        Task<LaneUndoReport?> UndoWindowAsync(string lane, string tier, bool apply, long personId, CancellationToken cancellationToken = default);
    }
}
