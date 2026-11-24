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

    /// <summary>Every scraped line of one wording on one curated food, and the curated food it should be on.</summary>
    public class LineRelinkWordingMove
    {
        public long FromIngredientId { get; set; }
        /// <summary>The normalised wording of the line, compared without regard to case.</summary>
        public string Wording { get; set; } = string.Empty;
        public long ToIngredientId { get; set; }
    }

    public class LineRelinkPreview
    {
        public int LinesExamined { get; set; }
        public int MoveCount { get; set; }
        /// <summary>Moves left out because they would lower a recipe's diet flags (<see cref="Nom.Orch.Services.Support.LineMoveDietGuard"/>).</summary>
        public int HeldByDiet { get; set; }
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
        /// <summary>Lines left on their food because the move would lower a recipe's diet flags (<see cref="Nom.Orch.Services.Support.LineMoveDietGuard"/>); counted by a dry run too.</summary>
        public int HeldByDiet { get; set; }
        /// <summary>The first moves the diet guard held, with the restriction types each keeps.</summary>
        public List<Nom.Orch.Services.Support.LineMoveHold> DietHolds { get; set; } = new();
        public int Remaining { get; set; }
        /// <summary>Requested wording moves left out because a food is not a live curated food or the target has no nutrition.</summary>
        public int Rejected { get; set; }
        /// <summary>A dry run's moves, one per food, wording and target with its lines, most lines first.</summary>
        public List<LineRelinkMove>? Planned { get; set; }
        /// <summary>A dry run's requested wording moves that were left out.</summary>
        public List<LineRelinkWordingMove>? RejectedMoves { get; set; }
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
        /// <summary>Density and item weight the main entry had none of and took over from the retired food.</summary>
        public int MeasuresInherited { get; set; }
    }

    public class FoodRenameResult
    {
        public bool Renamed { get; set; }
        /// <summary>The name the food goes by afterwards.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>The live food that already holds the name; nothing is renamed while one does.</summary>
        public long? HeldByIngredientId { get; set; }
        /// <summary>Recipe lines on the food that holds the name.</summary>
        public int HeldByLines { get; set; }
        /// <summary>Aliases of the new name taken off other foods, so the name leads to this food alone.</summary>
        public int AliasesRemoved { get; set; }
    }

    public class LineRelinkUndoResult
    {
        public bool Applied { get; set; }
        public int Restored { get; set; }
        public int FoodsRestored { get; set; }
        /// <summary>Rows that were taken apart into one row per source line and are joined again, their added lines removed.</summary>
        public int Refolded { get; set; }
        /// <summary>Compound lines that were split into one row per food and are one row again, their added rows removed.</summary>
        public int Unsplit { get; set; }
        /// <summary>Lines with alternatives that were moved to their first alternative and are back on the food they were on.</summary>
        public int AlternativesPutBack { get; set; }
        /// <summary>Pending link proposals the verified wording link withdrew that are open again.</summary>
        public int PicksRestored { get; set; }
        /// <summary>Link proposals of retired ingredients (pending, or rejected by no person) the verified wording link withdrew that are back.</summary>
        public int ProposalsRestored { get; set; }
        /// <summary>Journal entries whose line has since moved again, been edited or gone, or whose food cannot take its name back.</summary>
        public int Skipped { get; set; }
        /// <summary>Journal entries of a recipe rebuilt after them (<see cref="Nom.Orch.Services.RecipeReprocessService"/>): undo the rebuild first.</summary>
        public int SkippedRebuilt { get; set; }
        /// <summary>Line entries left with their line where it is because moving it back would lower a recipe's diet flags (<see cref="Nom.Orch.Services.Support.LineMoveDietGuard"/>); not counted in Skipped.</summary>
        public int HeldByDiet { get; set; }
        /// <summary>The first moves the diet guard held, with the restriction types each keeps.</summary>
        public List<Nom.Orch.Services.Support.LineMoveHold> DietHolds { get; set; } = new();
    }
}
