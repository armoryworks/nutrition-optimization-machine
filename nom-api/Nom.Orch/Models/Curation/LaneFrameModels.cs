using System;
using System.Collections.Generic;
using Nom.Data.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Models.Curation
{
    /// <summary>
    /// One decision a lane records: what it was about, its answer, the mode it decided in and
    /// whether it writes (a pick or a rewrite, not an abstention or a no-op), whatever the mode.
    /// <see cref="Signals"/> and <see cref="Verdicts"/> are stored as JSON.
    /// </summary>
    public sealed record LaneDecisionInput(
        string Lane,
        string Tier,
        string Subject,
        string? Decision,
        string Mode,
        bool Writes,
        string Signature,
        string? Stratum = null,
        object? Signals = null,
        object? Verdicts = null);

    /// <summary>
    /// What a lane hands the frame to evaluate one of its tiers: its signature now, its bars, the
    /// audit check's score on the golden rows, a problem with its golden record (null when the
    /// record is current and meets its floor), and any fault of its own that must pause it now
    /// (an auditor that cannot be reached, a canary false accept, rule-audit failures).
    /// </summary>
    public sealed record LaneEvaluateRequest(
        string Lane,
        string Tier,
        string Signature,
        LanePolicy Policy,
        AuditorScore? Auditor,
        string? GoldenProblem = null,
        IReadOnlyList<string>? PauseReasons = null);

    /// <summary>An auditor's answer on one decision: wrong, right, or null when it could not answer.</summary>
    public sealed record LaneAuditAnswer(bool? Wrong, string? Note = null);

    /// <summary>One audit pass: how many decisions it read, and their outcomes.</summary>
    public sealed record LaneAuditPassResult(int Available, int Sampled, int Passed, int Flagged, int Errors, int Unreadable);

    /// <summary>What a lane's undo did, or would do in a dry run.</summary>
    public sealed record LaneUndoResult(int Undone, int Skipped, int Remaining, string? Note = null);

    /// <summary>The dry run or apply of a lane's undo of its writes since its last clean window.</summary>
    public sealed record LaneUndoReport(string Lane, string Tier, DateTime SinceUtc, bool Applied, LaneUndoResult Result);

    /// <summary>One lane or tier of the frame as the Pipeline page shows it.</summary>
    public sealed record LaneFrameStatusModel
    {
        public string Lane { get; init; } = string.Empty;
        public string Tier { get; init; } = LaneTiers.Whole;
        public string Mode { get; init; } = LaneMode.Shadow;
        public string? Reason { get; init; }
        public DateTime Since { get; init; }
        public string? SignatureHash { get; init; }
        public DateTime? EvaluatedAt { get; init; }
        public DateTime? LastCleanAt { get; init; }
        public LaneFigures? Figures { get; init; }
        public int DecisionsLast24h { get; init; }
        public int WrittenLast24h { get; init; }

        /// <summary>Whether the lane registered an undo of its writes since its last clean window.</summary>
        public bool CanUndo { get; init; }
    }
}
