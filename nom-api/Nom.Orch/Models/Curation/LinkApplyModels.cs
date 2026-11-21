using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One applied link the undo takes back, or would take back.</summary>
    public class LinkApplyUndoSample
    {
        public long ProposalId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Wording { get; set; } = string.Empty;
        public long TargetIngredientId { get; set; }
        public string TargetName { get; set; } = string.Empty;
        public int LinesRestored { get; set; }
        public int LinesSkipped { get; set; }
        /// <summary>Lines of a recipe rebuilt since the link, left where the rebuild put them; counted in LinesSkipped too.</summary>
        public int LinesSkippedRebuilt { get; set; }
        /// <summary>Empty when the link is taken back; otherwise why it is left as it is.</summary>
        public string Skipped { get; set; } = string.Empty;
    }

    public class LinkApplyUndoResult
    {
        public bool Applied { get; set; }
        /// <summary>Applied links this call took back, or would take back with apply.</summary>
        public int Proposals { get; set; }
        /// <summary>Applied links left as they are: the wording's food cannot come back under its name, or a line moved since the link. They do not count towards max.</summary>
        public int ProposalsSkipped { get; set; }
        public int LinesRestored { get; set; }
        /// <summary>Lines no longer where the link put them (edited, deleted or moved since); with leaveMovedLines they stay where they are and their journal row is kept. A line an order-only pass moved to another position is followed there and taken back.</summary>
        public int LinesSkipped { get; set; }
        /// <summary>Lines of a recipe rebuilt since the link: the rebuild replaced them, so they stay where it put them, their journal row is kept, and the rest of the link is taken back. Counted in LinesSkipped too.</summary>
        public int SkippedRebuilt { get; set; }
        /// <summary>Wording foods the link had merged away that come back.</summary>
        public int FoodsRestored { get; set; }
        public int AliasesRemoved { get; set; }
        /// <summary>Aliases put back to the context or deleted state they had before the link.</summary>
        public int AliasesRestored { get; set; }
        /// <summary>Aliases changed since the link; they are left as they are.</summary>
        public int AliasesSkipped { get; set; }
        public int StatusesRestored { get; set; }
        /// <summary>USDA foods that stay curated because another applied link or a later change still stands on them; with another link, the status row moves to that link.</summary>
        public int StatusesKept { get; set; }
        public int NamesRestored { get; set; }
        public int MeasuresRestored { get; set; }
        public int NutrientsRemoved { get; set; }
        /// <summary>Open link proposals the merge had removed that are written back.</summary>
        public int ProposalsRestored { get; set; }
        /// <summary>Applied links in the window this call did not reach; skipped links are counted in ProposalsSkipped instead.</summary>
        public int Remaining { get; set; }
        public List<LinkApplyUndoSample> Samples { get; set; } = new();
    }

    /// <summary>One pending link the hold rules keep for a person.</summary>
    public class LinkHoldSample
    {
        public long ProposalId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Wording { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string Hold { get; set; } = string.Empty;
    }

    public class LinkHoldReport
    {
        /// <summary>Pending name matches and model picks the rules looked at.</summary>
        public int Examined { get; set; }
        public int Held { get; set; }
        /// <summary>Held picks already marked on their reason.</summary>
        public int AlreadyMarked { get; set; }
        /// <summary>Held picks by the first rule that holds them.</summary>
        public Dictionary<string, int> ByRule { get; set; } = new();
        /// <summary>Held picks by source (deterministic:name-match, ai:&lt;model&gt;).</summary>
        public Dictionary<string, int> BySource { get; set; } = new();
        public List<LinkHoldSample> Samples { get; set; } = new();
    }
}
