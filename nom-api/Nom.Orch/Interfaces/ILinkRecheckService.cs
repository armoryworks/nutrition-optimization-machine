using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The re-check of links already made (plan C3): it withdraws every waiting model pick so the
    /// link lane asks again through the gate, and puts the lines of applied model links, trusted
    /// aliases, alcohol words, cans and prepared foods through the link gate by rule set, moving a
    /// line to the food a deterministic tier names or back to an uncurated food of its wording when
    /// the gate refuses its food. Every run is a dry run unless applied, is journaled under its own
    /// stamp and is undone by that stamp, skipping recipes rebuilt since. On the lane frame as lane
    /// <c>link-recheck</c>, one tier per rule set and one per re-check class that is not proven, whose
    /// candidates are written only while that class's tier is live. See docs/architecture/link-recheck.md.
    /// </summary>
    public interface ILinkRecheckService
    {
        /// <summary>Withdraws every pending model link pick no person reviewed, one journal entry each under one stamp; a dry run counts them.</summary>
        Task<LinkRecheckWithdrawResult> WithdrawPicksAsync(bool apply = false, int sampleSize = 50, CancellationToken cancellationToken = default);

        /// <summary>Puts back the picks one withdrawal took, by its stamp, unless a pick changed since or its ingredient has a newer open link proposal.</summary>
        Task<LinkRecheckWithdrawUndoResult> UndoWithdrawAsync(DateTime stamp, bool apply = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs one rule set (<see cref="LinkRecheckRules.AllSets"/>) over at most <paramref name="max"/>
        /// wordings, most lines first. Unless <paramref name="apply"/> it writes nothing. A candidate is
        /// written (or counted, in a dry run) only while its class's tier is live. Null for an unknown set.
        /// </summary>
        Task<LinkRecheckResult?> RunAsync(string set, bool apply = false, int max = 2000, int sampleSize = 50, CancellationToken cancellationToken = default);

        /// <summary>Takes back one run by its stamp; a dry run counts what it would take back.</summary>
        Task<LinkRecheckUndoResult> UndoAsync(DateTime stamp, bool apply = false, CancellationToken cancellationToken = default);

        /// <summary>Lists curated foods whose name their USDA entry contradicts and renames those the fixed table names, journaled under one stamp.</summary>
        Task<LinkRecheckNameResult> CheckNamesAsync(bool apply = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// One lane batch of a set on the lane frame: decides the wordings not decided under the set's
        /// signature in the last 30 days (a candidate recorded in shadow is decided again once its class's
        /// tier is live), records every decision and writes a proven class's refutations only in live
        /// mode and a candidate only while its class's tier is live, each class under a stamp of its own.
        /// </summary>
        Task<LinkRecheckResult> RunLaneBatchAsync(string set, string mode, int max, CancellationToken cancellationToken = default);

        /// <summary>Audits a sample of the tier's moves (a set's, or a class's candidates) with the given auditor and check and evaluates the tier with the batch's faults and the link-audit record scored with that auditor and check.</summary>
        Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string set, IReadOnlyList<string> faults, ILaneAuditor? auditor, ILaneAuditCheck? check, CancellationToken cancellationToken = default);

        /// <summary>Decisions the lane recorded for a set in the last 24 hours, its candidates on the class tiers included.</summary>
        Task<int> DecidedTodayAsync(string set, CancellationToken cancellationToken = default);

        Task<LinkRecheckStatusModel> StatusAsync(bool enabled, CancellationToken cancellationToken = default);
    }
}
