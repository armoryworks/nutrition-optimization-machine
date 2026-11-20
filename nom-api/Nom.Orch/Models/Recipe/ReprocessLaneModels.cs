using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>One batch of the reprocess lane: what it took, what the rebuild did, and what the rule checks found.</summary>
    public class ReprocessLaneBatchResult
    {
        public string Tier { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public int Taken { get; set; }
        public int Rebuilt { get; set; }
        public int Held { get; set; }
        public int Unchanged { get; set; }
        public int Failed { get; set; }
        public int ApprovedTaken { get; set; }
        public int ApprovedHeld { get; set; }

        /// <summary>Changed lines the rule checks read, and those any of A1 to A3 failed on; a recipe failing A4 or A5 counts as one more failing line.</summary>
        public int LinesChecked { get; set; }
        public int LinesFailing { get; set; }

        public Dictionary<string, int> FailuresByCheck { get; set; } = new();
        public Dictionary<string, int> HeldByReason { get; set; } = new();

        /// <summary>Why the lane must pause now, or stay in shadow: rule checks under their bar, too many approved recipes held, a rebuild that is not idempotent.</summary>
        public List<string> Faults { get; set; } = new();

        public double? RuleShare => LinesChecked == 0 ? null : (double)(LinesChecked - LinesFailing) / LinesChecked;
    }

    /// <summary>One changed line as a lane decision keeps it, for the line judge and the rule audit.</summary>
    public sealed record ReprocessSignalLine(
        string Raw, string? OldFood, decimal? OldQuantity, string? OldUnit, string? OldKind,
        string NewFood, decimal NewQuantity, string? NewUnit, string? NewKind,
        bool FilledFromMethod, bool FoodChanged, decimal? GramsPerItem, string Method, IReadOnlyList<string> Changes, IReadOnlyList<string> Failures);

    /// <summary>The signals of one reprocess lane decision.</summary>
    public sealed record ReprocessSignals(
        long RecipeId, string Recipe, string Outcome, string? Reason, IReadOnlyList<string> Reasons, string BatchId, long? JournalId,
        bool Worse, IReadOnlyList<ReprocessSignalLine> Lines);

    /// <summary>The reprocess lane on the Pipeline page: candidates by reason and tier, what the lane did, the last batch per tier and the line judge.</summary>
    public class ReprocessLaneStatusModel
    {
        public bool Enabled { get; set; }
        public string RuleVersion { get; set; } = string.Empty;
        public int Read { get; set; }
        public int ReadWithCurrentRules { get; set; }
        public int Waiting { get; set; }
        public Dictionary<string, int> WaitingByTier { get; set; } = new();
        public List<ReprocessReasonCount> ByReason { get; set; } = new();
        public Dictionary<string, int> ByOutcome { get; set; } = new();
        public Dictionary<string, int> HeldByReason { get; set; } = new();
        public DateTime? LastDetectedAt { get; set; }
        public string JudgeModel { get; set; } = string.Empty;
        public string? JudgeProblem { get; set; }
        public List<ReprocessLaneBatchResult> LastBatches { get; set; } = new();
    }

    /// <summary>Recipes found with one reason, and how many of them still wait for the lane.</summary>
    public sealed record ReprocessReasonCount(string Code, string Label, int Recipes, int Waiting);
}
