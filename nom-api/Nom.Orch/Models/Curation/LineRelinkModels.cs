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

    public class FoodRetireResult
    {
        public bool Retired { get; set; }
        /// <summary>Recipe lines still on the food; it is not retired while there are any.</summary>
        public int LinesLeft { get; set; }
        /// <summary>A pantry, shopping list, plan, restriction, substitution or component still points at the food.</summary>
        public bool InUseElsewhere { get; set; }
        /// <summary>The name the main entry goes by afterwards.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>The name that now leads to the main entry.</summary>
        public string Alias { get; set; } = string.Empty;
        /// <summary>Aliases of that name taken off other foods, so the name leads to the main entry alone.</summary>
        public int AliasesRemoved { get; set; }
    }

    public class LineRelinkUndoResult
    {
        public int Restored { get; set; }
        public int FoodsRestored { get; set; }
        /// <summary>Journal entries whose line has since moved again or gone, or whose food cannot take its name back.</summary>
        public int Skipped { get; set; }
    }
}
