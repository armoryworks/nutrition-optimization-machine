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
        public int Recovered { get; set; }
        public int RecoveredRetracted { get; set; }
        public int LinesMoved { get; set; }
        public List<string> Batches { get; set; } = new();
        public Dictionary<string, List<string>> FaultsByTier { get; set; } = new();

        /// <summary>The tiers that decided anything in this pass.</summary>
        public HashSet<string> Tiers { get; set; } = new();
    }

    /// <summary>What one tier would do in an operator's dry run: its wordings and their lines, held, refused and would-be writes.</summary>
    public sealed class WordingTierCoverage
    {
        public int Wordings { get; set; }
        public int Lines { get; set; }
        public int Held { get; set; }
        public int Refused { get; set; }
        public int WouldWrite { get; set; }
        public int WouldWriteLines { get; set; }

        /// <summary>Share of the wordings read that this tier would write.</summary>
        public double WordingShare { get; set; }

        /// <summary>Share of the lines read that this tier would move.</summary>
        public double LineShare { get; set; }
    }

    /// <summary>One wording of an operator's dry run, as it would be decided.</summary>
    public sealed class WordingTierSample
    {
        public string Wording { get; set; } = string.Empty;
        public long SourceId { get; set; }
        public int Lines { get; set; }
        public string Tier { get; set; } = string.Empty;
        public long? FoodId { get; set; }
        public string? FoodName { get; set; }
        public string? Detail { get; set; }

        /// <summary>Why the wording would not be written: a gate hold, a retraction, a family hold or the loader's refusal; null for a write.</summary>
        public string? Hold { get; set; }

        public List<string> SampleLines { get; set; } = new();
    }

    /// <summary>
    /// An operator's dry run of the wording tiers over the waiting wordings: coverage per tier, holds,
    /// abstentions and samples. Calls no model, records no decision and writes nothing.
    /// </summary>
    public sealed class WordingTierPreview
    {
        public string Signature { get; set; } = string.Empty;
        public int Waiting { get; set; }
        public int Skip { get; set; }
        public int Read { get; set; }
        public int ReadLines { get; set; }
        public int NotKeyForm { get; set; }
        public int Abstained { get; set; }
        public int AbstainedLines { get; set; }
        public Dictionary<string, int> AbstainedByReason { get; set; } = new();
        public Dictionary<string, int> HeldByRule { get; set; } = new();
        public Dictionary<string, int> RefusedByReason { get; set; } = new();
        public Dictionary<string, WordingTierCoverage> ByTier { get; set; } = new();
        public Dictionary<string, List<WordingTierSample>> Samples { get; set; } = new();
    }
}
