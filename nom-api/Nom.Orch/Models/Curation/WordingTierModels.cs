using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>
    /// What the wording tier lane keeps with one decision (in <c>LaneDecision.SignalsJson</c>): the
    /// wording and its harder key, the uncurated ingredient it is on, its line count, the tier and
    /// food, the evidence key the row is loaded with, the batch, every tier's answer, the rules that
    /// held it and a few of its lines. The auditor reads the pair from it and the lane's undo finds
    /// the row by it.
    /// </summary>
    public sealed record WordingTierSignals(
        string Wording,
        string? Key,
        long SourceId,
        int Lines,
        string Tier,
        long? FoodId,
        string? FoodName,
        string? FoodText,
        string? EvidenceKey,
        string? Batch,
        IReadOnlyList<string> Answers,
        string? Hold,
        IReadOnlyList<string> SampleLines);

    /// <summary>One pass of the wording tier lane over the wordings it took.</summary>
    public sealed class WordingTierBatchResult
    {
        public DateTime Stamp { get; set; }
        public Dictionary<string, string> Modes { get; set; } = new();
        public int Read { get; set; }
        public int NotKeyForm { get; set; }
        public int Abstained { get; set; }
        public Dictionary<string, int> AbstainedByReason { get; set; } = new();
        public int Held { get; set; }
        public Dictionary<string, int> HeldByRule { get; set; } = new();
        public Dictionary<string, int> WritesByTier { get; set; } = new();
        public Dictionary<string, int> WrittenByTier { get; set; } = new();
        public Dictionary<string, int> RefusedByTier { get; set; } = new();
        public Dictionary<string, int> RefusedByReason { get; set; } = new();
        public int Capped { get; set; }
        public int LinesMoved { get; set; }
        public List<string> Batches { get; set; } = new();
        public Dictionary<string, List<string>> FaultsByTier { get; set; } = new();

        /// <summary>The tiers that decided anything in this pass.</summary>
        public HashSet<string> Tiers { get; set; } = new();
    }
}
