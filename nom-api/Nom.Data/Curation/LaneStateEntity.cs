using System;

namespace Nom.Data.Curation
{
    /// <summary>
    /// The mode of one automated lane, or of one tier of it, kept in the database so a pause
    /// holds across a restart. A lane that writes runs in shadow (decides and records, writes
    /// nothing), live, or paused; no row means shadow, so nothing goes live by a deploy alone.
    /// Only the lane frame (<c>LaneFrameService</c>) changes a row, and every change of mode is
    /// journaled in <c>audit.AuditLogEntry</c> (LaneQualify, LanePause, LaneResume, LaneShadow).
    /// See docs/architecture/lane-frame.md.
    /// </summary>
    public class LaneStateEntity : BaseEntity
    {
        /// <summary>The lane's name, as the lane gate and its settings use it.</summary>
        public string Lane { get; set; } = string.Empty;

        /// <summary>The tier within the lane; <see cref="LaneTiers.Whole"/> for a lane without tiers or for the whole lane.</summary>
        public string Tier { get; set; } = LaneTiers.Whole;

        /// <summary>shadow | live | paused — see <see cref="LaneMode"/>.</summary>
        public string Mode { get; set; } = LaneMode.Shadow;

        /// <summary>Hash of what decides: models, prompt versions, rules version. A change sends a live lane back to shadow.</summary>
        public string? SignatureHash { get; set; }

        /// <summary>The signature the lane had when it paused; a lane whose policy asks for a new signature resumes only under another one.</summary>
        public string? PausedSignatureHash { get; set; }

        /// <summary>The figures of the last evaluation (counts, corrected error, bounds, bars, auditor scores), as JSON.</summary>
        public string? FiguresJson { get; set; }

        /// <summary>Why the lane is in its mode, in words.</summary>
        public string? Reason { get; set; }

        /// <summary>When the lane entered its mode.</summary>
        public DateTime Since { get; set; }

        /// <summary>Decisions before this time do not count toward qualifying; set when the signature changes and when the lane pauses.</summary>
        public DateTime WindowStart { get; set; }

        /// <summary>The newest decision a clean audit window covered; a pause undoes the lane's writes after it.</summary>
        public DateTime? LastCleanAt { get; set; }

        public DateTime? EvaluatedAt { get; set; }
    }

    /// <summary>The modes of <see cref="LaneStateEntity.Mode"/>.</summary>
    public static class LaneMode
    {
        public const string Shadow = "shadow";
        public const string Live = "live";
        public const string Paused = "paused";
    }

    /// <summary>Tier names the frame itself uses.</summary>
    public static class LaneTiers
    {
        /// <summary>The whole lane; a paused whole-lane row pauses every tier of the lane.</summary>
        public const string Whole = "*";
    }
}
