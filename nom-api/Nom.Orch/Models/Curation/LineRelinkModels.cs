using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>Recipe lines of one wording that would move from one curated food to another.</summary>
    public class LineRelinkMove
    {
        public long FromIngredientId { get; set; }
        public string FromName { get; set; } = string.Empty;
        public string Wording { get; set; } = string.Empty;
        public long ToIngredientId { get; set; }
        public string ToName { get; set; } = string.Empty;
        public int Lines { get; set; }
    }

    public class LineRelinkPreview
    {
        public int LinesExamined { get; set; }
        public int MoveCount { get; set; }
        /// <summary>The moves with the most lines first.</summary>
        public List<LineRelinkMove> Samples { get; set; } = new();
    }

    public class LineRelinkResult
    {
        public int Moved { get; set; }
        /// <summary>Moved lines that took a new position because the recipe already had the target food at theirs.</summary>
        public int Repositioned { get; set; }
        /// <summary>Lines that were no longer where the plan found them.</summary>
        public int Skipped { get; set; }
        public int Remaining { get; set; }
    }

    public class LineRelinkUndoResult
    {
        public int Restored { get; set; }
        /// <summary>Journal entries whose line has since moved again or gone.</summary>
        public int Skipped { get; set; }
    }
}
