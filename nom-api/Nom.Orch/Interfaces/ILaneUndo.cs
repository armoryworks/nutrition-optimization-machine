using System;
using System.Threading;
using System.Threading.Tasks;
using Nom.Data.Curation;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// A lane's undo of its own writes, which the lane frame calls when the lane pauses: first as
    /// a dry run, which is logged and journaled, then applied. The undo journals what it reverses
    /// in the lane's own journal, so it can itself be undone. Registered by the lane in DI.
    /// </summary>
    public interface ILaneUndo
    {
        /// <summary>The lane whose writes this undoes.</summary>
        string Lane { get; }

        /// <summary>Reverses the tier's writes since <paramref name="sinceUtc"/>; with <paramref name="apply"/> false, only counts them.</summary>
        Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken);
    }

    /// <summary>The auditor of a lane: a model of a family no reader of the lane uses, reading a sampled decision.</summary>
    public interface ILaneAuditor
    {
        string Model { get; }

        Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The second, independent check of a decision the auditor flagged: a reader of another family
    /// re-reading it blind, or a rule audit. A flag counts as an error only when this check agrees.
    /// </summary>
    public interface ILaneAuditCheck
    {
        string Name { get; }

        /// <summary>True when the check also finds the decision wrong, false when it finds it right, null when it cannot tell.</summary>
        Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken);
    }
}
