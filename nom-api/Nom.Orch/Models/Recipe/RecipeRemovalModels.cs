using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// What a removal stage did, or, as a dry run, would do: the retire of recipes taken from forbidden sources, its undo
    /// by stamp, or the purge of their stored source wording. A dry run and its apply give the same figures.
    /// </summary>
    public class RecipeRemovalReportModel
    {
        public string Stage { get; set; } = string.Empty;
        public bool Applied { get; set; }
        public DateTime Stamp { get; set; }
        public long? SourceId { get; set; }
        public long? RunId { get; set; }

        /// <summary>The apply carried on a run an API restart cut off, under that run's stamp and scope.</summary>
        public bool Resumed { get; set; }

        public string? Refused { get; set; }

        /// <summary>The recipes the stage acts on (dry run: would act on).</summary>
        public int Recipes { get; set; }

        public int Skipped { get; set; }
        public int AlreadyDone { get; set; }
        public Dictionary<string, int> SkippedByReason { get; set; } = new();
        public List<RecipeRemovalSourceModel> Sources { get; set; } = new();

        /// <summary>For a purge: every table and column it deletes or clears, with the rows it touches.</summary>
        public List<RecipeRemovalTargetModel> Targets { get; set; } = new();

        public string? Warning { get; set; }

        /// <summary>
        /// For a retire: the foods named from the source's lines (no FDC id, not curated) that no other recipe's live line
        /// uses and nothing a household or another food relies on holds, retired with the recipes (journaled, put back by the
        /// undo until the purge). For its undo: the foods it puts back.
        /// </summary>
        public int Foods { get; set; }

        /// <summary>The first 500 recipe ids the stage acts on, in id order.</summary>
        public List<long> RecipeIds { get; set; } = new();
    }

    /// <summary>One source's share of a removal stage, with what households and people hold of its recipes.</summary>
    public class RecipeRemovalSourceModel
    {
        public long SourceId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string? RightsClass { get; set; }
        public string? RightsQuote { get; set; }
        public string? RightsQuoteUrl { get; set; }
        public int Recipes { get; set; }
        public int AlreadyDeleted { get; set; }
        public int Approved { get; set; }
        public int ApprovedByPerson { get; set; }
        public int ApprovedUnrecorded { get; set; }
        public int EditedByPerson { get; set; }
        public int InMealPlans { get; set; }
        public int Favourited { get; set; }
        public int InCookbooks { get; set; }
        public int InShoppingLists { get; set; }
    }

    /// <summary>One table and its columns a purge deletes (whole rows) or clears (the named columns), with the rows it touches.</summary>
    public class RecipeRemovalTargetModel
    {
        public string Table { get; set; } = string.Empty;
        public string Columns { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public int Rows { get; set; }
    }

    public class RecipeRemovalRunModel
    {
        public long Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public long? SourceId { get; set; }
        public DateTime? UndoesStamp { get; set; }
        public long PersonId { get; set; }
        public string? Reason { get; set; }
        public int Planned { get; set; }
        public int Done { get; set; }
        public int Skipped { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }

        /// <summary>Retired recipes of this run that are still retired and not purged: what its undo would put back.</summary>
        public int Undoable { get; set; }
    }

    /// <summary>Per source: recipes still taken, retired (not undone, purged ones included) and purged.</summary>
    public class RecipeRemovalFiguresModel
    {
        public long SourceId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string? RightsClass { get; set; }

        /// <summary>The source's effective class is forbidden on every row of its host, so its recipes may be removed.</summary>
        public bool Removable { get; set; }

        public int Taken { get; set; }
        public int Retired { get; set; }
        public int Purged { get; set; }
    }

    public class RecipeRemovalStatusModel
    {
        public int Retired { get; set; }
        public int Purged { get; set; }
        public List<RecipeRemovalFiguresModel> BySource { get; set; } = new();
        public List<RecipeRemovalRunModel> Runs { get; set; } = new();
        public RecipeRemovalRunModel? Unfinished { get; set; }
    }

    /// <summary>A recipe a household still holds (in a meal plan, a favourite list, a cookbook or a shopping list) that was removed: its title and source link, never its wording.</summary>
    public sealed record RemovedRecipeModel(long RecipeId, string Title, string SourceUrl);
}
