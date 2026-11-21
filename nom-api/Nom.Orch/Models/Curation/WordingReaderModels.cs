using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>
    /// What the wording reader lane keeps with one decision (in <c>LaneDecision.SignalsJson</c>): the
    /// wording, the uncurated ingredient it is on, its line count, the tier, the verdict and food,
    /// the evidence key and batch the row is loaded with, both readers' answers, the outcome rule,
    /// the rules that held it, a few of its lines, an agreed meaning and, in the fixed-reading mode,
    /// the mode and the reading. For a lexicon decision the wording is the term and the food text is
    /// what it means. The auditor reads the pair from it and the lane's undo finds the row by it.
    /// </summary>
    public sealed record WordingReaderSignals(
        string Wording,
        long SourceId,
        int Lines,
        string Tier,
        string? Verdict,
        long? FoodId,
        string? FoodName,
        string? FoodText,
        string? EvidenceKey,
        string? Batch,
        string? AnswerA,
        string? AnswerB,
        string Rule,
        string? Hold,
        IReadOnlyList<string> SampleLines,
        string? Means = null,
        string? Mode = null,
        string? Reading = null);

    /// <summary>One pass of the wording reader lane over the wordings it took.</summary>
    public sealed class WordingReaderBatchResult
    {
        public DateTime Stamp { get; set; }
        public Dictionary<string, string> Modes { get; set; } = new();
        public int Read { get; set; }
        public int NotLeftByTiers { get; set; }
        public int Compound { get; set; }
        public int Calls { get; set; }
        public Dictionary<string, int> OutcomeByRule { get; set; } = new();
        public int Held { get; set; }
        public Dictionary<string, int> HeldByRule { get; set; } = new();
        public Dictionary<string, int> WritesByTier { get; set; } = new();
        public Dictionary<string, int> WrittenByTier { get; set; } = new();
        public Dictionary<string, int> RefusedByReason { get; set; } = new();
        public int RowsNone { get; set; }
        public int RowsUnclear { get; set; }
        public int Capped { get; set; }
        public int LexiconProposed { get; set; }
        public int LexiconRefuted { get; set; }
        public int LexiconWritten { get; set; }
        public int LinesMoved { get; set; }
        public int SentinelJudged { get; set; }
        public int SentinelDifferent { get; set; }
        public int Retracted { get; set; }
        public List<string> Batches { get; set; } = new();
        public Dictionary<string, List<string>> FaultsByTier { get; set; } = new();

        /// <summary>Both readers' agreement on the wordings both read readably, which a drift check compares with the golden run.</summary>
        public int Agreed { get; set; }
        public int BothRead { get; set; }

        /// <summary>The tiers that decided anything in this pass.</summary>
        public HashSet<string> Tiers { get; set; } = new();
    }
}
