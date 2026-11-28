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

        /// <summary>Listed in <see cref="Changes"/> when the line guard kept the stored amount for the line judge (<see cref="Guard"/> is "hold").</summary>
        public const string Held = "held";

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

        /// <summary>
        /// The line guard's verdict on the amount (<see cref="Nom.Orch.Services.Support.LineAmountGuard"/>): kept, replaced or held; null when
        /// the line has no stored row or the built amount is the stored one. A kept or held line keeps the stored
        /// amount and kind; a held one names the built amount in <see cref="HeldQuantity"/> for the line judge.
        /// </summary>
        public string? Guard { get; set; }

        /// <summary>Why the guard gave its verdict (<see cref="Nom.Orch.Services.Support.LineAmountGuard.Reasons"/>); a held line is stored as a hold with it on an apply.</summary>
        public string? GuardReason { get; set; }

        public decimal? HeldQuantity { get; set; }
        public long? HeldMeasurementId { get; set; }
        public string? HeldUnit { get; set; }
        public string? HeldLineKind { get; set; }
        public bool HeldFromMethod { get; set; }

        /// <summary>What changed: any of amount, food, order, kind, text, added; empty when nothing did. Also prepared-food (see <see cref="PreparedFood"/>) and held (the guard kept a stored amount the line judge is to read).</summary>
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

        /// <summary>Stored amounts the line guard kept against an empty or ungrounded built one.</summary>
        public int GuardKept { get; set; }

        /// <summary>Stored amounts, empty or contradicting the source, the line guard let a grounded built one replace.</summary>
        public int GuardReplaced { get; set; }

        /// <summary>Stored amounts the line guard kept for the line judge: both read the source and differ, or the stored one contradicts it with nothing grounded in its place.</summary>
        public int GuardHeld { get; set; }

        /// <summary>The held amounts by the guard's reason.</summary>
        public Dictionary<string, int> GuardHeldByReason { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Stored amounts emptied on a recipe the rebuild held, because each is only the number of the recipe or page its line refers to.</summary>
        public int ReferralsCleared { get; set; }

        /// <summary>Adds only the line guard's counts, as for a recipe left unchanged because every line that differs is kept or held.</summary>
        public void AddGuard(RecipeReprocessChangeCounts other)
        {
            GuardKept += other.GuardKept;
            GuardReplaced += other.GuardReplaced;
            GuardHeld += other.GuardHeld;
            ReferralsCleared += other.ReferralsCleared;
            foreach (var (reason, count) in other.GuardHeldByReason) GuardHeldByReason[reason] = GuardHeldByReason.GetValueOrDefault(reason) + count;
        }

        public void Add(RecipeReprocessChangeCounts other)
        {
            AddGuard(other);
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

        /// <summary>On a held recipe, the lines whose stored amount is, or would be, emptied as only a referred recipe's or page's number.</summary>
        public List<RecipeReprocessLineDiff> ReferralLines { get; set; } = new();
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

        /// <summary>
        /// Order-only writes whose lines a later step changed in a food link, an amount or a kind, but not in
        /// position: the positions came back, the later change was kept, and the move back is journaled as an
        /// order write of its own so the later step's undo still finds its lines.
        /// </summary>
        public int PositionsFollowed { get; set; }

        /// <summary>Foods a rebuild created that nothing references once its lines are restored; removed again.</summary>
        public int FoodsRemoved { get; set; }

        /// <summary>Foods a rebuild created that something still references or someone changed since; kept.</summary>
        public int FoodsKept { get; set; }

        /// <summary>Deleted foods whose name a rebuild freed for a food it created ("name (deleted #id)"), given their name back once that food is removed (staging checkpoint 9, U10).</summary>
        public int NamesRestored { get; set; }

        public Dictionary<string, int> RestoredByKind { get; set; } = new();
        public List<long> SkippedRecipeIds { get; set; } = new();

        /// <summary>The apply run this call belongs to: its progress is read by its id, and a stopped run goes on under it.</summary>
        public string? RunId { get; set; }

        /// <summary>Batches this call took; each batch of an apply is one transaction and one progress row.</summary>
        public int Batches { get; set; }

        /// <summary>Entries this call did not reach because it stopped; 0 when it reached its last entry.</summary>
        public int Remaining { get; set; }

        /// <summary>Why the call ended before its last entry (<see cref="RecipeReprocessUndoStop"/>), or null when it reached the end.</summary>
        public string? Stopped { get; set; }

        /// <summary>Why the call did not start, or null when it ran.</summary>
        public string? Refused { get; set; }

        /// <summary>Entries listed for this call that something else took back before their batch was read; they are not judged again.</summary>
        public int AlreadyUndone { get; set; }
    }

    /// <summary>Why a reprocess undo ended between two batches before its last entry.</summary>
    public static class RecipeReprocessUndoStop
    {
        /// <summary>An operator asked the run to stop.</summary>
        public const string StopRequested = "stop-requested";

        /// <summary>The caller went away (the request was aborted).</summary>
        public const string ClientGone = "client-gone";

        /// <summary>The call took as many batches as it was given.</summary>
        public const string MaxBatches = "max-batches";
    }

    /// <summary>What a reprocess undo takes back and how it runs.</summary>
    public class RecipeReprocessUndoRequest
    {
        public DateTime? SinceUtc { get; set; }
        public string? BatchId { get; set; }
        public string? Kind { get; set; }
        public bool Apply { get; set; }
        public bool LaneOnly { get; set; }

        /// <summary>An earlier run to go on with: its filter is used, and it starts after the last entry that run reached.</summary>
        public string? RunId { get; set; }

        /// <summary>Journal entries per batch.</summary>
        public int BatchSize { get; set; } = 500;

        /// <summary>The most batches this call takes; null for all.</summary>
        public int? MaxBatches { get; set; }
    }

    /// <summary>How far a reprocess undo run has come, from its progress rows in the audit log.</summary>
    public class RecipeReprocessUndoProgress
    {
        /// <summary>The state of the run.</summary>
        public static class States
        {
            /// <summary>A call of this API process is taking its batches now.</summary>
            public const string Running = "running";

            /// <summary>It ended before its last entry and can go on under its id.</summary>
            public const string Stopped = "stopped";

            /// <summary>It reached its last entry.</summary>
            public const string Finished = "finished";

            /// <summary>Its last call ended without an end row (the API stopped); every batch written is whole, so it can go on under its id.</summary>
            public const string Interrupted = "interrupted";
        }

        public string RunId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;

        /// <summary>The last end row's reason: <c>finished</c> or a <see cref="RecipeReprocessUndoStop"/> value.</summary>
        public string? Outcome { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? SinceUtc { get; set; }
        public string? BatchId { get; set; }
        public string? Kind { get; set; }
        public bool LaneOnly { get; set; }

        /// <summary>Journal entries the run had to take back when it started.</summary>
        public int EntriesAtStart { get; set; }
        public int Batches { get; set; }

        /// <summary>Entries the run has read, restored or skipped.</summary>
        public int Reached { get; set; }

        /// <summary>Entries still to take back after the last one reached, counted now.</summary>
        public int Remaining { get; set; }
        public int Restored { get; set; }
        public int Skipped { get; set; }
        public int PositionsFollowed { get; set; }
        public int StateKept { get; set; }
        public int FoodsRemoved { get; set; }
        public int FoodsKept { get; set; }
        public int NamesRestored { get; set; }

        /// <summary>Entries reached that something else had taken back before their batch was read.</summary>
        public int AlreadyUndone { get; set; }
        public Dictionary<string, int> RestoredByKind { get; set; } = new();

        /// <summary>The id of the last journal entry reached; the run goes on with the entries below it.</summary>
        public long? LastEntryId { get; set; }
        public DateTime? LastBatchAt { get; set; }

        /// <summary>The time the run's batches took, summed; the time between a stop and the call that went on is not counted.</summary>
        public double WorkingSeconds { get; set; }

        /// <summary>Entries reached per minute of <see cref="WorkingSeconds"/>.</summary>
        public double? EntriesPerMinute { get; set; }
        public bool StopRequested { get; set; }
    }

    /// <summary>One recipe the detector marks, as an operator reads it.</summary>
    public class ReprocessDetectSample
    {
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Approved { get; set; }
        public List<string> Reasons { get; set; } = new();
        public string Tier { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string? Hold { get; set; }
        public List<RecipeReprocessLineDiff> Lines { get; set; } = new();
    }

    /// <summary>The answer of an operator's detector run: what the lane's sweep would mark, by reason.</summary>
    public class ReprocessDetectReport
    {
        public bool Applied { get; set; }
        public string RuleVersion { get; set; } = string.Empty;
        public int Read { get; set; }
        public int Candidates { get; set; }
        public int ApprovedCandidates { get; set; }

        /// <summary>Candidates the light order-only pass would take (only the order would change).</summary>
        public int OrderTier { get; set; }

        /// <summary>Candidates only a full rebuild reaches.</summary>
        public int RebuildTier { get; set; }

        public Dictionary<string, int> ByReason { get; set; } = new();
        public Dictionary<string, int> HeldByReason { get; set; } = new();

        /// <summary>The last recipe read; the next call starts after it. Null when nothing was left to read.</summary>
        public long? LastRecipeId { get; set; }

        public List<ReprocessDetectSample> Samples { get; set; } = new();
    }
}
