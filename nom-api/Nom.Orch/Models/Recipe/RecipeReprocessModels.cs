using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>The recipes a rebuild or a duplicate retire looks at.</summary>
    public class RecipeReprocessRequestModel
    {
        public List<long> RecipeIds { get; set; } = new();
    }

    /// <summary>What happened to one recipe of a rebuild.</summary>
    public static class RecipeReprocessOutcome
    {
        /// <summary>The lines were built again (or would be, in a dry run).</summary>
        public const string Rebuilt = "rebuilt";

        /// <summary>The lines built from the source equal the stored lines; nothing is written.</summary>
        public const string Unchanged = "unchanged";

        /// <summary>A guard kept the stored lines; <see cref="RecipeReprocessRecipeResult.Reason"/> says which.</summary>
        public const string Held = "held";

        /// <summary>The write failed and was rolled back.</summary>
        public const string Failed = "failed";
    }

    /// <summary>Why a recipe was held. Each is a reason the stored lines are kept as they are.</summary>
    public static class RecipeReprocessHold
    {
        public const string NotFound = "not-found";
        public const string NoSource = "no-source";
        public const string SourceUnreadable = "source-unreadable";
        public const string SourceEmpty = "source-empty";
        public const string Rejected = "rejected";
        public const string PersonEdit = "person-edit";
        public const string CompoundSplit = "compound-split";
        public const string OrderUnpaired = "order-unpaired";
        public const string PlannedFoodChange = "planned-food-change";
        public const string ApprovedFewerAmounts = "approved-fewer-amounts";
        public const string ApprovedFewerCurated = "approved-fewer-curated";
        public const string ApprovedVetting = "approved-vetting";
        public const string ApprovedGate = "approved-gate";
        public const string PersonStepEdit = "person-step-edit";
        public const string ProseRewritten = "prose-rewritten";
    }

    /// <summary>One line of a rebuilt recipe next to the stored row it replaces (none for an added line).</summary>
    public class RecipeReprocessLineDiff
    {
        /// <summary>
        /// Listed in <see cref="Changes"/> when the line ends on a prepared food: its text is that
        /// food's whole name, so the import's name match leads there and no other food can take the
        /// name. The approval gate holds such a recipe; renaming the food is for a person.
        /// </summary>
        public const string PreparedFood = "prepared-food";

        public int SortOrder { get; set; }
        public string RawLine { get; set; } = string.Empty;
        public int? OldSortOrder { get; set; }
        public long? OldIngredientId { get; set; }
        public string? OldFood { get; set; }
        public long? NewIngredientId { get; set; }
        public string NewFood { get; set; } = string.Empty;
        public decimal? OldQuantity { get; set; }
        public decimal NewQuantity { get; set; }
        public long? OldMeasurementId { get; set; }
        public long NewMeasurementId { get; set; }
        public string? OldLineKind { get; set; }
        public string? NewLineKind { get; set; }
        public bool FilledFromMethod { get; set; }

        /// <summary>What changed: any of amount, food, order, kind, text, added; empty when nothing did. Also prepared-food (see <see cref="PreparedFood"/>).</summary>
        public List<string> Changes { get; set; } = new();
    }

    /// <summary>The counts of what a rebuild changes on one recipe or a whole batch.</summary>
    public class RecipeReprocessChangeCounts
    {
        public int LinesBefore { get; set; }
        public int LinesAfter { get; set; }
        public int AmountChanged { get; set; }
        public int FoodChanged { get; set; }
        public int OrderChanged { get; set; }
        public int KindChanged { get; set; }
        public int Added { get; set; }
        public int Dropped { get; set; }
        public int FilledFromMethod { get; set; }
        public int NewFoods { get; set; }
        public int OnPreparedFood { get; set; }

        public void Add(RecipeReprocessChangeCounts other)
        {
            LinesBefore += other.LinesBefore;
            LinesAfter += other.LinesAfter;
            AmountChanged += other.AmountChanged;
            FoodChanged += other.FoodChanged;
            OrderChanged += other.OrderChanged;
            KindChanged += other.KindChanged;
            Added += other.Added;
            Dropped += other.Dropped;
            FilledFromMethod += other.FilledFromMethod;
            NewFoods += other.NewFoods;
            OnPreparedFood += other.OnPreparedFood;
        }
    }

    /// <summary>What a rebuild did, or would do, to one recipe.</summary>
    public class RecipeReprocessRecipeResult
    {
        public long RecipeId { get; set; }
        public string Outcome { get; set; } = RecipeReprocessOutcome.Unchanged;
        public string? Reason { get; set; }
        public string? SourceFormat { get; set; }
        public long? SourceDocumentId { get; set; }

        /// <summary>Set to Rejected by rule: neither the source nor the rebuilt version is a recipe.</summary>
        public bool Rejected { get; set; }

        /// <summary>The steps are, or would be, replaced by those of a newer extraction of the book.</summary>
        public bool StepsReplaced { get; set; }

        /// <summary>The source's ingredient lines the pass read, in order.</summary>
        public List<string>? SourceLines { get; set; }

        public long? JournalId { get; set; }
        public RecipeReprocessChangeCounts Counts { get; set; } = new();
        public List<RecipeReprocessLineDiff> Lines { get; set; } = new();
    }

    /// <summary>The answer of a rebuild or an order-only pass over a batch of recipes.</summary>
    public class RecipeReprocessResult
    {
        public bool Applied { get; set; }
        public bool OrderOnly { get; set; }
        public string BatchId { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public string RuleVersion { get; set; } = string.Empty;
        public int Recipes { get; set; }
        public int Rebuilt { get; set; }
        public int Unchanged { get; set; }
        public int Held { get; set; }
        public int Failed { get; set; }
        public int Rejected { get; set; }
        public Dictionary<string, int> HeldByReason { get; set; } = new();
        public RecipeReprocessChangeCounts Counts { get; set; } = new();
        public List<RecipeReprocessRecipeResult> Results { get; set; } = new();
    }

    /// <summary>The recipes a duplicate retire looks at, and whether a name alone (any attribution) groups them.</summary>
    public class RecipeDuplicateRequestModel
    {
        /// <summary>Only groups whose recipes are all in this list; every recipe when empty.</summary>
        public List<long> RecipeIds { get; set; } = new();

        /// <summary>Group by name alone, for a book imported twice under two attributions; still only when the lines agree.</summary>
        public bool AnyAttribution { get; set; }
    }

    /// <summary>One copy of a duplicate group and what happened to it.</summary>
    public class RecipeDuplicateCopy
    {
        public long RecipeId { get; set; }
        public long KeptRecipeId { get; set; }
        public string GroupedBy { get; set; } = string.Empty;
        public double Agreement { get; set; }

        /// <summary>retired, or held with <see cref="Reason"/>.</summary>
        public string Outcome { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public long? JournalId { get; set; }
    }

    /// <summary>The answer of a duplicate retire.</summary>
    public class RecipeDuplicateResult
    {
        public const string Retired = "retired";
        public const string Held = "held";
        public const string HeldReferenced = "referenced";
        public const string HeldPersonEdit = "person-edit";
        public const string HeldLinesDiffer = "lines-differ";

        public bool Applied { get; set; }
        public string BatchId { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public int Groups { get; set; }
        public int RetiredCount { get; set; }
        public int HeldCount { get; set; }
        public List<RecipeDuplicateCopy> Copies { get; set; } = new();
    }

    /// <summary>The answer of a reprocess undo.</summary>
    public class RecipeReprocessUndoResult
    {
        public bool Applied { get; set; }
        public int Entries { get; set; }
        public int Restored { get; set; }

        /// <summary>The recipe's lines or state changed after the write, so it was left as it is.</summary>
        public int Skipped { get; set; }

        /// <summary>Lines restored, while the recipe's curation state, changed since, was kept.</summary>
        public int StateKept { get; set; }

        /// <summary>Foods a rebuild created that nothing references once its lines are restored; removed again.</summary>
        public int FoodsRemoved { get; set; }

        /// <summary>Foods a rebuild created that something still references or someone changed since; kept.</summary>
        public int FoodsKept { get; set; }

        public Dictionary<string, int> RestoredByKind { get; set; } = new();
        public List<long> SkippedRecipeIds { get; set; } = new();
    }
}
