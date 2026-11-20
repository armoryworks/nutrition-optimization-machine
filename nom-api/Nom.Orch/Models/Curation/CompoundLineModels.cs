using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>What one compound-line rule does across the catalog.</summary>
    public class CompoundLineRuleCount
    {
        public string Rule { get; set; } = string.Empty;
        public int Lines { get; set; }
        /// <summary>Lines on an ingredient that is not curated; they land on curated foods.</summary>
        public int OnUncurated { get; set; }
        public int OnCurated { get; set; }
        public int OnApprovedRecipes { get; set; }
        /// <summary>Rows that move to another food.</summary>
        public int RowMoves { get; set; }
        public int RowsAdded { get; set; }
        /// <summary>Moved or added rows placed at the end of the recipe because the food already holds the line's position.</summary>
        public int Repositioned { get; set; }
    }

    /// <summary>One row a compound line becomes.</summary>
    public class CompoundLinePart
    {
        public string RawLine { get; set; } = string.Empty;
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        /// <summary>0 when the part line states no amount the parser reads.</summary>
        public decimal Quantity { get; set; }
        public string Measurement { get; set; } = string.Empty;
    }

    /// <summary>The lines of one rule and wording, an example of them and the rows that example becomes.</summary>
    public class CompoundLineSample
    {
        public string Rule { get; set; } = string.Empty;
        public string Wording { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public int Lines { get; set; }
        public List<CompoundLinePart> Parts { get; set; } = new();
    }

    public class CompoundLinePreview
    {
        /// <summary>Scraped ingredient lines whose text holds "and", "or", "&amp;" or "plus".</summary>
        public int LinesExamined { get; set; }
        /// <summary>Lines the chosen rules act on.</summary>
        public int Lines { get; set; }
        /// <summary>Joined "a + b" lines that a rule would act on once they are unfolded.</summary>
        public int FoldedDeferred { get; set; }
        public List<CompoundLineRuleCount> Rules { get; set; } = new();
        /// <summary>Compound lines left as they are, by reason.</summary>
        public Dictionary<string, int> LeftAlone { get; set; } = new();
        /// <summary>The wordings with the most lines first.</summary>
        public List<CompoundLineSample> Samples { get; set; } = new();
    }

    public class CompoundLineResult
    {
        /// <summary>Lines that became one row per food.</summary>
        public int Split { get; set; }
        /// <summary>Lines moved to their first alternative.</summary>
        public int Moved { get; set; }
        public int RowsAdded { get; set; }
        /// <summary>Moved or added rows placed at the end of the recipe because the food already holds the line's position.</summary>
        public int Repositioned { get; set; }
        /// <summary>Lines that were no longer as the plan found them.</summary>
        public int Skipped { get; set; }
        /// <summary>Lines the chosen rules still act on after this call.</summary>
        public int Remaining { get; set; }
    }
}
