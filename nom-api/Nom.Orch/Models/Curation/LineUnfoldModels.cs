using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One line a joined row is read back into, with the amount it holds afterwards.</summary>
    public class LineUnfoldLine
    {
        public string RawLine { get; set; } = string.Empty;
        /// <summary>0 when the line states no amount the parser reads.</summary>
        public decimal Quantity { get; set; }
        public string Measurement { get; set; } = string.Empty;
    }

    /// <summary>One joined row and the lines it becomes; the first line is the row itself.</summary>
    public class LineUnfoldSample
    {
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;
        public List<LineUnfoldLine> Lines { get; set; } = new();
    }

    public class LineUnfoldResult
    {
        public bool Applied { get; set; }
        /// <summary>Live lines of scraped recipes whose text contains " + ", whatever their kind.</summary>
        public int RowsExamined { get; set; }
        /// <summary>Joined rows this call took apart, or would take apart with apply.</summary>
        public int Folds { get; set; }
        public int LinesAdded { get; set; }
        /// <summary>Folds that became one line again because every further part repeated it without an amount.</summary>
        public int Collapsed { get; set; }
        /// <summary>Parts left out because they repeat an earlier part of their row and state no amount.</summary>
        public int DuplicatesDropped { get; set; }
        /// <summary>Folds whose stored amount was a later part's, so it went with that line and the first line is unmeasured.</summary>
        public int QuantitiesReassigned { get; set; }
        /// <summary>Added lines with no amount the parser reads.</summary>
        public int UnmeasuredAdded { get; set; }
        /// <summary>Rows that are one line of their recipe's source list ("1 cup + 2 Tablespoons sugar").</summary>
        public int SkippedSingleSourceLine { get; set; }
        /// <summary>Rows whose parts cannot be told apart safely: they do not add up to lines of the recipe's stored source, a part is only an amount and there is no stored source, or the text is too long to journal.</summary>
        public int SkippedAmbiguous { get; set; }
        /// <summary>Rows classified as a reference, equipment, note, instruction or artefact.</summary>
        public int SkippedOtherKind { get; set; }
        /// <summary>Folds still joined after this call.</summary>
        public int Remaining { get; set; }
        public List<LineUnfoldSample> Samples { get; set; } = new();
    }
}
