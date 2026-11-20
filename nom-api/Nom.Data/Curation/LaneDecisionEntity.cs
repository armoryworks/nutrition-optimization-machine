using System;

namespace Nom.Data.Curation
{
    /// <summary>
    /// One decision of an automated lane, in shadow or live: what it was about, the signals and
    /// verdicts it rested on, whether it was written, and what the audit found. The lane frame
    /// counts the audited rows to qualify a lane and to pause it. Rows are evidence only; the
    /// lane's own write and its journal are elsewhere.
    /// </summary>
    public class LaneDecisionEntity : BaseEntity
    {
        public string Lane { get; set; } = string.Empty;

        public string Tier { get; set; } = LaneTiers.Whole;

        /// <summary>What was decided about, e.g. <c>wording:123</c> or <c>recipe:45</c>.</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>The answer in short, e.g. a food id or "none".</summary>
        public string? Decision { get; set; }

        /// <summary>The lane's mode when it decided — see <see cref="LaneMode"/>.</summary>
        public string Mode { get; set; } = LaneMode.Shadow;

        /// <summary>Whether the lane wrote the decision; only a live lane writes.</summary>
        public bool Written { get; set; }

        /// <summary>The lane's signature when it decided.</summary>
        public string? SignatureHash { get; set; }

        /// <summary>Group the audit sample is stratified by, e.g. a head word or a food group.</summary>
        public string? Stratum { get; set; }

        /// <summary>Each signal's answer, as JSON.</summary>
        public string? SignalsJson { get; set; }

        /// <summary>The readers' and checks' verdicts, as JSON.</summary>
        public string? VerdictsJson { get; set; }

        /// <summary>null until audited; then pass | flagged | error | unreadable — see <see cref="LaneAuditOutcome"/>.</summary>
        public string? AuditOutcome { get; set; }

        /// <summary>The auditor's and the second check's answers, as JSON.</summary>
        public string? AuditJson { get; set; }

        public DateTime? AuditedAt { get; set; }
    }

    /// <summary>
    /// The outcomes of <see cref="LaneDecisionEntity.AuditOutcome"/>. An auditor flag counts as an
    /// error only when a second, independent check agrees; a flag the second check does not
    /// confirm is kept as flagged and counts as audited and right.
    /// </summary>
    public static class LaneAuditOutcome
    {
        public const string Pass = "pass";
        public const string Flagged = "flagged";
        public const string Error = "error";
        public const string Unreadable = "unreadable";
    }
}
