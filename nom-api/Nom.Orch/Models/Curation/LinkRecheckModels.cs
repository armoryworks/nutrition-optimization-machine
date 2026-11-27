using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One waiting model pick the withdrawal takes (or would take) out of the queue.</summary>
    public class LinkRecheckPickSample
    {
        public long ProposalId { get; set; }
        public long? IngredientId { get; set; }
        public string Wording { get; set; } = string.Empty;
        public string? TargetId { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// The withdrawal of every waiting model link pick: how many were found and withdrawn, by
    /// source, and the run's stamp, which its undo takes.
    /// </summary>
    public class LinkRecheckWithdrawResult
    {
        public bool Applied { get; set; }
        public DateTime? Stamp { get; set; }
        public int Examined { get; set; }
        public int Withdrawn { get; set; }
        public Dictionary<string, int> BySource { get; set; } = new();
        public List<LinkRecheckPickSample> Samples { get; set; } = new();
    }

    /// <summary>The undo of one withdrawal: picks put back to the queue, and those left because they changed or their wording was asked again since.</summary>
    public class LinkRecheckWithdrawUndoResult
    {
        public bool Applied { get; set; }
        public DateTime Stamp { get; set; }
        public int Entries { get; set; }
        public int Restored { get; set; }
        public int Skipped { get; set; }
        public int SkippedAskedAgain { get; set; }
    }

    /// <summary>One wording on one food and what the re-check does, or would do, with its lines.</summary>
    public class LinkRecheckSample
    {
        public string Outcome { get; set; } = string.Empty;
        public string? Class { get; set; }
        public string? Candidate { get; set; }
        public long FoodId { get; set; }
        public string FoodName { get; set; } = string.Empty;
        public string Wording { get; set; } = string.Empty;
        public string? Sense { get; set; }
        public int Lines { get; set; }
        public string? Reason { get; set; }
        public string? Tier { get; set; }
        public long? TargetId { get; set; }
        public string? TargetName { get; set; }
        public long? WordingRowId { get; set; }
        public List<string> Holds { get; set; } = new();
        public List<string> ExampleLines { get; set; } = new();
    }

    /// <summary>
    /// One run of a re-check rule set: the wordings and lines it read, what it kept, what it kept
    /// because the gate's holds do not prove the food wrong (not proven, by rule), what it moved to
    /// another food or took back to an uncurated food of their wording because a hold refutes the
    /// food (refuted) by a proven re-check class, what a class not proven would refute (candidates,
    /// kept and handed to the model audit), the candidates it wrote because their class's tier is
    /// live on the lane frame (by class; a lane batch writes each class under a stamp of its own), the foods it created or renamed and the names it freed, the aliases it
    /// moved, untrusted, removed or added, the run's stamp (its undo takes it), the verified
    /// wording rows whose lines it took off a prepared food (each to be retracted) and samples by
    /// outcome. A dry run counts all of these by the apply's own steps.
    /// </summary>
    public class LinkRecheckResult
    {
        public string Set { get; set; } = string.Empty;
        public bool Applied { get; set; }
        public DateTime? Stamp { get; set; }
        public string Signature { get; set; } = string.Empty;
        public bool Lane { get; set; }
        public string? Mode { get; set; }
        public int Groups { get; set; }
        public int Lines { get; set; }
        public int Kept { get; set; }
        public int Held { get; set; }
        public int Unproven { get; set; }
        public int UnprovenLines { get; set; }
        public int Candidates { get; set; }
        public int CandidateLines { get; set; }
        public int RefutedLines { get; set; }
        public int Verified { get; set; }
        public int Alternatives { get; set; }
        public int MoveGroups { get; set; }
        public int UnlinkGroups { get; set; }
        public int LinesMoved { get; set; }
        public int LinesUnlinked { get; set; }
        public int LinesSkipped { get; set; }
        public int NoHome { get; set; }
        /// <summary>Groups taken back to a wording food of unknown contents because the take-back would lower a flag its words do not account for (staging checkpoint 7, S15); counted in UnlinkGroups too.</summary>
        public int UnknownGroups { get; set; }
        /// <summary>Wording foods marked of unknown contents (<see cref="Nom.Orch.Services.Support.RecheckDietGuard.UnknownContentsContext"/>), or that a dry run would mark.</summary>
        public int FoodsMarkedUnknown { get; set; }
        public int AliasesMoved { get; set; }
        public int AliasesUntrusted { get; set; }
        public int AliasesRemoved { get; set; }
        public int AliasesAdded { get; set; }
        public int AliasesRefused { get; set; }
        public int FoodsCreated { get; set; }
        public int FoodsRenamed { get; set; }
        public int NamesFreed { get; set; }
        public int ApprovedRecipes { get; set; }
        public int Remaining { get; set; }
        public List<long> WordingRows { get; set; } = new();
        public Dictionary<string, int> ByReason { get; set; } = new();
        public Dictionary<string, int> ByUnproven { get; set; } = new();
        public Dictionary<string, int> ByCandidate { get; set; } = new();
        public Dictionary<string, int> ByClass { get; set; } = new();
        public Dictionary<string, int> ByQualified { get; set; } = new();
        public List<DateTime> ClassStamps { get; set; } = new();
        public Dictionary<string, int> ByTier { get; set; } = new();
        public List<LinkRecheckSample> Samples { get; set; } = new();
    }

    /// <summary>What a run's journal header keeps: the set, the stamp, who ran it, its counts and whether it ran to its end (a run cut off keeps the header written before its first write); the status adds whether a run not complete is still going in this process.</summary>
    public class LinkRecheckRunSummary
    {
        public string Set { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public bool Lane { get; set; }
        public string? Mode { get; set; }
        public string Signature { get; set; } = string.Empty;
        public bool Undone { get; set; }
        public bool Complete { get; set; }
        public bool Running { get; set; }
        public int Groups { get; set; }
        public int Kept { get; set; }
        public int Held { get; set; }
        public int Unproven { get; set; }
        public int UnprovenLines { get; set; }
        public int Candidates { get; set; }
        public int CandidateLines { get; set; }
        public int LinesMoved { get; set; }
        public int LinesUnlinked { get; set; }
        public int AliasesChanged { get; set; }
        public int Withdrawn { get; set; }
        public int Renamed { get; set; }
    }

    /// <summary>
    /// The undo of one re-check run by its stamp: the lines put back (a line on a recipe rebuilt
    /// since stays), the aliases restored, the foods it created removed and the names it freed or
    /// changed given back; for a withdrawal, the picks put back. A dry run counts what its apply
    /// would do by the same checks.
    /// </summary>
    public class LinkRecheckUndoResult
    {
        public bool Applied { get; set; }
        public DateTime Stamp { get; set; }
        public string? Set { get; set; }
        public bool Found { get; set; }
        public int LineEntries { get; set; }
        public int LinesRestored { get; set; }
        public int OnRebuiltRecipes { get; set; }
        public int SkippedRebuilt { get; set; }
        public int Skipped { get; set; }
        /// <summary>Lines left where the run put them because moving them back would lower a recipe's diet flags (<see cref="Nom.Orch.Services.Support.LineMoveDietGuard"/>); counted by a dry run too.</summary>
        public int HeldByDiet { get; set; }
        /// <summary>Whether the operator chose to restore the exact journaled earlier state, lowering diet flags where the way back lowers them (never by default).</summary>
        public bool RestoreLowered { get; set; }
        /// <summary>With <see cref="RestoreLowered"/>: the lines put back (or, in a dry run, that would be) although their way back lowers a recipe's diet flags; each is journaled on the apply.</summary>
        public int RestoredLowering { get; set; }
        /// <summary>With <see cref="RestoreLowered"/>: the first such moves, with the restriction types each lowers.</summary>
        public List<Nom.Orch.Services.Support.LineMoveHold> LoweringMoves { get; set; } = new();
        public int NamesRestored { get; set; }
        public int AliasEntries { get; set; }
        public int AliasesRestored { get; set; }
        /// <summary>Marks of unknown contents kept with their journal entries because the undo left lines the run moved on their food (<see cref="HeldByDiet"/>): without the mark those lines would lower the flags the severe types hold them by (review ck16-r1). Lines that were on the food before the run never keep it: an undo that takes back every move of the run takes back every mark (staging checkpoint 8, T13).</summary>
        public int MarksKept { get; set; }
        public int FoodsRemoved { get; set; }
        public int FoodsKept { get; set; }
        public int PickEntries { get; set; }
        public int PicksRestored { get; set; }
        public int SkippedAskedAgain { get; set; }
    }

    /// <summary>A curated food whose name its USDA entry contradicts, and the name a fixed rule gives it, if any.</summary>
    public class LinkRecheckNameRow
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? FdcId { get; set; }
        public string? NewName { get; set; }
        public bool Renamed { get; set; }
    }

    /// <summary>The catalog-name check: the foods listed, the ones renamed by the fixed table, and the stamp.</summary>
    public class LinkRecheckNameResult
    {
        public bool Applied { get; set; }
        public DateTime? Stamp { get; set; }
        public int Renamed { get; set; }
        public List<LinkRecheckNameRow> Listed { get; set; } = new();
    }

    /// <summary>The re-check as the Pipeline page shows it: on or off, the rules' version and signature, the classes that may write by rule (and why none may, when the per-class record is not current), and the newest run of each set.</summary>
    public class LinkRecheckStatusModel
    {
        public bool Enabled { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public List<string> Sets { get; set; } = new();
        public List<string> ProvenClasses { get; set; } = new();
        public List<string> CandidateClasses { get; set; } = new();
        public string? ProvenProblem { get; set; }
        public List<LinkRecheckRunSummary> LastRuns { get; set; } = new();
        public LinkRecheckRunSummary? LastWithdraw { get; set; }
        public int WaitingPicks { get; set; }
    }
}
