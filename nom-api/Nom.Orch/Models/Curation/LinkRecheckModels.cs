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
    /// One run of a re-check rule set: the wordings and lines it read, what it kept, moved to
    /// another food and took back to an uncurated food of their wording, the aliases it moved,
    /// untrusted, removed or added, the run's stamp (its undo takes it), the verified wording rows
    /// whose lines it took off a prepared food (each to be retracted) and samples by outcome.
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
        public int Verified { get; set; }
        public int Alternatives { get; set; }
        public int MoveGroups { get; set; }
        public int UnlinkGroups { get; set; }
        public int LinesMoved { get; set; }
        public int LinesUnlinked { get; set; }
        public int LinesSkipped { get; set; }
        public int NoHome { get; set; }
        public int AliasesMoved { get; set; }
        public int AliasesUntrusted { get; set; }
        public int AliasesRemoved { get; set; }
        public int AliasesAdded { get; set; }
        public int FoodsCreated { get; set; }
        public int ApprovedRecipes { get; set; }
        public int Remaining { get; set; }
        public List<long> WordingRows { get; set; } = new();
        public Dictionary<string, int> ByReason { get; set; } = new();
        public Dictionary<string, int> ByTier { get; set; } = new();
        public List<LinkRecheckSample> Samples { get; set; } = new();
    }

    /// <summary>What a run's journal header keeps: the set, the stamp, who ran it, its counts and whether it ran to its end (a run cut off keeps the header written before its first write).</summary>
    public class LinkRecheckRunSummary
    {
        public string Set { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public bool Lane { get; set; }
        public string? Mode { get; set; }
        public string Signature { get; set; } = string.Empty;
        public bool Undone { get; set; }
        public bool Complete { get; set; }
        public int Groups { get; set; }
        public int Kept { get; set; }
        public int Held { get; set; }
        public int LinesMoved { get; set; }
        public int LinesUnlinked { get; set; }
        public int AliasesChanged { get; set; }
        public int Withdrawn { get; set; }
        public int Renamed { get; set; }
    }

    /// <summary>
    /// The undo of one re-check run by its stamp: the lines put back (a line on a recipe rebuilt
    /// since stays), the aliases restored, the foods it created removed and the names it freed or
    /// changed given back.
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
        public int NamesRestored { get; set; }
        public int AliasEntries { get; set; }
        public int AliasesRestored { get; set; }
        public int FoodsRemoved { get; set; }
        public int FoodsKept { get; set; }
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

    /// <summary>The re-check as the Pipeline page shows it: on or off, the rules' version and signature, and the newest run of each set.</summary>
    public class LinkRecheckStatusModel
    {
        public bool Enabled { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public List<string> Sets { get; set; } = new();
        public List<LinkRecheckRunSummary> LastRuns { get; set; } = new();
        public LinkRecheckRunSummary? LastWithdraw { get; set; }
        public int WaitingPicks { get; set; }
    }
}
