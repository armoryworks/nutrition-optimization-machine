using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Npgsql;
using NpgsqlTypes;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The diet gate's build job (staging checkpoint 5, Q2, Q3): brings the stored results of the current rule version
    /// (<see cref="DietGateStore.Version"/>) up to date. A whole cycle (<see cref="RunAsync"/>) reads the food catalog once
    /// (<see cref="DietFoodCatalog"/>) and decides, with the matcher's own reading (<see cref="DietFoodReader"/>), every
    /// food whose fingerprint changed or that has no stored state (every food for a set not yet built); it reads every
    /// live recipe line in windows of recipe ids and reads with <see cref="DietLineRules"/> only the recipes it decides
    /// again: per restriction set, every recipe whose lines or servings changed, that has no stored state, or one of whose
    /// foods changed (every recipe for a set not yet built), with the retained alcohol counted for those recipes only
    /// (<see cref="DietCriteriaMatcher.RecipeAlcoholAsync"/>). A cycle over the changes (<see cref="RunChangesAsync"/>)
    /// reads only the foods and recipes a writer made undecided or logged (the triggers of <c>db/custom-objects.sql</c>
    /// remove their stored state and log the write in the writer's own transaction, staging checkpoint 6, R12) and the
    /// recipes on those foods. A recipe's
    /// flags and state are replaced together, a chunk of recipes in one transaction. A new rule version is built as a new
    /// build beside the active one, which requests keep reading until the new build is complete; the old build is then
    /// retired and its rows removed. The job keeps only the fingerprints and the sets' food facts in memory between
    /// cycles (R9) and reloads them from the stored rows after a restart.
    /// </summary>
    public sealed class DietGateStoreBuilder
    {
        /// <summary>How many recipe ids one read of the lines covers.</summary>
        public const int LineWindow = 2000;

        /// <summary>How many recipes are decided and written in one transaction.</summary>
        public const int RecipeChunk = 5000;

        /// <summary>How many rows of the write log (<c>plan."DietGateWrite"</c>) one cycle reads; the rest wait for the next.</summary>
        public const int WriteBatch = 500_000;

        /// <summary>
        /// The most recipes a cycle over the changes reads by their ids; above it the cycle reads every line in windows of
        /// recipe ids as a whole cycle does (staging checkpoint 7, S2), which costs about the same whatever the scope.
        /// </summary>
        public const int LargeScope = 5000;

        /// <summary>What one cycle did: the build, the sets (and those built in this cycle, with their timings), the foods and recipes decided again, and the time each part took.</summary>
        public sealed record CycleReport(long BuildId, string Version, string State, bool Switched, int Sets, int FoodsDecided, int Foods, int RecipesDecided, int Recipes,
            double FoodSeconds, double RecipeSeconds, double Seconds, IReadOnlyList<DietGateSetReport> Built)
        {
            /// <summary>The recipes decided but not stored because a write to them reached the write log after their lines were read; they stay undecided for the next cycle.</summary>
            public int RecipesLate { get; init; }

            /// <summary>Of the foods decided, those whose decision moved for some set (or whose retained alcohol inputs moved): only recipes on these are decided again for a write to a food (staging checkpoint 7, S2).</summary>
            public int FoodsMoved { get; init; }

            /// <summary>The recipes a write to one of their foods held, released without being read again because no decision of those foods moved.</summary>
            public int RecipesConfirmed { get; init; }

            /// <summary>Whether a cycle over the changes read every line in windows because its scope was larger than <see cref="LargeScope"/>.</summary>
            public bool Windowed { get; init; }
        }

        internal sealed record Spec(long TypeId, int MinimumSeverity, string Key, string Text, bool Severe, List<DietDataCriterion> Criteria);

        internal sealed record Fact(DietVerdict Verdict, long? CriterionId, string? CriterionIds, string? Source);

        internal sealed record Reading(long Fingerprint, string[] Rules, DietCriteriaMatcher.RecipeLine[] Drinks, long[] Foods, int Lines, string[] TitleRules, string[] MethodRules)
        {
            public LineWords[] Words { get; init; } = Array.Empty<LineWords>();

            public (int Word, int Next)[] MethodWords { get; init; } = Array.Empty<(int, int)>();

            public string? MethodFood { get; init; }

            public string? Book { get; init; }

            public long? ConsommeLine { get; init; }

            public bool DoughLost { get; init; }

            public bool FlourLost { get; init; }

            public DietFoodWords.Table? Table { get; init; }
        }

        internal sealed record LineWords(long IngredientId, int[] Words, bool Read, int Group = -1, bool Reference = false, bool Choice = false);

        internal sealed record Words(DietFoodWords.Table Table, long Signature)
        {
            public IReadOnlyDictionary<string, int> Groups { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

            public IReadOnlyList<CategoryWording> Rows { get; init; } = Array.Empty<CategoryWording>();

            public IReadOnlyList<(string Book, string Clarifier)> Clarifiers { get; init; } = Array.Empty<(string, string)>();

            public IReadOnlyList<(long Food, string Wording)> UnprovenRows { get; init; } = Array.Empty<(long, string)>();

            public IReadOnlyDictionary<long, UnprovenLinks> Unproven { get; init; } = new Dictionary<long, UnprovenLinks>();
        }

        /// <summary>The wordings an unproven lane linked to one food, as the verified relink reads a line's wording: the keys themselves and the exact keys each one stands for.</summary>
        internal sealed record UnprovenLinks(HashSet<string> Wordings, HashSet<string> Keys);

        internal sealed record Flag(DietVerdict Verdict, DietFlagReason Reason, long? IngredientId, long? CriterionId);

        internal sealed record LineRow(long RecipeId, long IngredientId, int SortOrder, string? RawLine, decimal Quantity, long MeasurementId, string? LineKind, decimal? AlcoholRetention, string? AlcoholRetentionBasis);

        internal sealed record RecipeLines(Dictionary<long, string> Servings, Dictionary<long, List<LineRow>> Lines, Dictionary<long, BookText> Books);

        internal sealed record BookText(string? Name, string? Method, string? Book = null);

        internal sealed record Readers(DietLineRules.Matcher Lines, DietLineRules.Matcher Method, DietLineRules.Matcher Echo, Dictionary<string, DietPatternRule?> RuleOf);

        internal sealed class SetState
        {
            public SetState(DietGateSetEntity row) => Row = row;

            public DietGateSetEntity Row { get; }

            public Spec Spec { get; set; } = default!;

            public Dictionary<long, Fact> Facts { get; } = new();

            public Dictionary<string, long> LineKeys { get; set; } = new(StringComparer.Ordinal);

            public Dictionary<string, long> AllKeys { get; set; } = new(StringComparer.Ordinal);

            public long? DrinkCriterion { get; set; }

            public DietDataCriterion? Capped { get; set; }

            public DietFoodWords.Vocabulary? Vocabulary { get; set; }

            public DietFoodWords.WordClass[] GroupClasses { get; set; } = Array.Empty<DietFoodWords.WordClass>();

            public HashSet<string> ClarifiedBooks { get; set; } = new(StringComparer.Ordinal);

            public bool Building => Row.State == DietGateStore.Building;

            public Stopwatch FoodClock { get; } = new();

            public Stopwatch RecipeClock { get; } = new();

            public int Containing { get; set; }

            public int Unknown { get; set; }
        }

        private readonly Func<string> _version;
        private long _buildId = -1;
        internal Dictionary<long, long> FoodPrints => _foodPrints;

        private readonly Dictionary<long, long> _foodPrints = new();
        private readonly Dictionary<long, long> _recipePrints = new();
        private readonly Dictionary<long, long> _foodInputs = new();
        private readonly Dictionary<long, SetState> _sets = new();
        private Words? _words;

        /// <summary>Run after the foods of a cycle are decided and stored and before its recipes are (a test makes a cycle fail there).</summary>
        internal Func<CancellationToken, Task>? BeforeRecipes { get; set; }

        /// <summary>The scope above which a cycle over the changes reads every line in windows (<see cref="LargeScope"/>; a test lowers it).</summary>
        internal int LargeScopeLimit { get; set; } = LargeScope;

        /// <summary>Run after the lines of a cycle are read and before its recipes are decided (a test writes a line there).</summary>
        internal Func<CancellationToken, Task>? AfterLinesRead { get; set; }

        /// <summary>A build job for the current rule version, or for the version <paramref name="version"/> gives.</summary>
        public DietGateStoreBuilder(Func<string>? version = null) => _version = version ?? (() => DietGateStore.Version);

        /// <summary>
        /// Whether a committed write to what the gate reads waits in the write log (<c>plan."DietGateWrite"</c>, written by
        /// the triggers of <c>db/custom-objects.sql</c> in the writer's own transaction, whatever process wrote, so a row is
        /// seen exactly when its write commits, review CK11-DM-1).
        /// </summary>
        public static Task<bool> PendingWritesAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
            db.DietGateWrites.AsNoTracking().AnyAsync(cancellationToken);

        private sealed record Writes(List<long> Ids, HashSet<long> Recipes, HashSet<long> Foods, HashSet<long> FoodRecipes);

        private sealed record LogMark(string? Snapshot, HashSet<long>? Seen)
        {
            public ulong? Xmin => Snapshot == null ? null : ulong.Parse(Snapshot[..Snapshot.IndexOf(':')], CultureInfo.InvariantCulture);
        }

        private static async Task<LogMark> MarkAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            if (db.Database.IsRelational() && db.Database.GetDbConnection() is NpgsqlConnection)
                return new LogMark((await db.Database.SqlQueryRaw<string>("SELECT pg_current_snapshot()::text AS \"Value\"").ToListAsync(cancellationToken)).Single(), null);
            return new LogMark(null, (await db.DietGateWrites.AsNoTracking().Select(w => w.Id).ToListAsync(cancellationToken)).ToHashSet());
        }

        private static async Task<HashSet<long>> LateAsync(ApplicationDbContext db, HashSet<long> seen, IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken) =>
            (await db.DietGateWrites.AsNoTracking()
                    .Where(w => w.RecipeId != null && recipeIds.Contains(w.RecipeId.Value))
                    .Select(w => new { w.Id, RecipeId = w.RecipeId!.Value })
                    .ToListAsync(cancellationToken))
                .Where(w => !seen.Contains(w.Id))
                .Select(w => w.RecipeId)
                .ToHashSet();

        private static async Task<HashSet<long>> WithdrawLateAsync(ApplicationDbContext db, long buildId, long[] setIds, long[] recipeIds, string snapshot, CancellationToken cancellationToken) =>
            (await db.Database.SqlQueryRaw<long>(
                    "WITH late AS (SELECT DISTINCT w.\"RecipeId\" AS id FROM plan.\"DietGateWrite\" w WHERE w.\"RecipeId\" = ANY(@recipes) AND NOT pg_visible_in_snapshot(w.\"Tx\", @snapshot::pg_snapshot)), " +
                    "flags AS (DELETE FROM plan.\"DietRecipeFlag\" f USING late WHERE f.\"RecipeId\" = late.id AND f.\"SetId\" = ANY(@sets)) " +
                    "DELETE FROM plan.\"DietRecipeState\" s USING late WHERE s.\"BuildId\" = @build AND s.\"RecipeId\" = late.id RETURNING s.\"RecipeId\" AS \"Value\"",
                    new NpgsqlParameter("recipes", recipeIds), new NpgsqlParameter("snapshot", snapshot), new NpgsqlParameter("sets", setIds), new NpgsqlParameter("build", buildId))
                .ToListAsync(cancellationToken)).ToHashSet();

        /// <summary>
        /// Removes the rows of the write log older than <paramref name="age"/>, for a process that runs no build
        /// (<c>DietGate:Build=false</c>): the triggers log every write whether or not a build job reads the rows, and a build
        /// job anywhere reads a row within a cycle. Returns the number removed.
        /// </summary>
        public static async Task<int> TrimWritesAsync(ApplicationDbContext db, TimeSpan age, CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow - age;
            var old = db.DietGateWrites.Where(w => w.WrittenAt < before);
            if (db.Database.IsRelational()) return await old.ExecuteDeleteAsync(cancellationToken);
            var found = await old.ToListAsync(cancellationToken);
            db.DietGateWrites.RemoveRange(found);
            await db.SaveChangesAsync(cancellationToken);
            return found.Count;
        }

        private static async Task<Writes> ReadWritesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var rows = await db.DietGateWrites.AsNoTracking().OrderBy(w => w.Id).Take(WriteBatch)
                .Select(w => new { w.Id, w.RecipeId, w.IngredientId })
                .ToListAsync(cancellationToken);
            return new Writes(rows.Select(r => r.Id).ToList(),
                rows.Where(r => r.RecipeId != null && r.IngredientId == null).Select(r => r.RecipeId!.Value).ToHashSet(),
                rows.Where(r => r.IngredientId != null).Select(r => r.IngredientId!.Value).ToHashSet(),
                rows.Where(r => r.RecipeId != null && r.IngredientId != null).Select(r => r.RecipeId!.Value).ToHashSet());
        }

        private static async Task ConsumeWritesAsync(ApplicationDbContext db, Writes writes, CancellationToken cancellationToken)
        {
            foreach (var batch in writes.Ids.Chunk(5000))
                await DeleteAsync(db, db.DietGateWrites.Where(w => batch.Contains(w.Id)), cancellationToken);
        }

        /// <summary>Runs one whole cycle on the context's database: every food's and every recipe's fingerprint is read and compared with what is stored.</summary>
        public async Task<CycleReport> RunAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
        {
            try
            {
                return await CycleAsync(db, cancellationToken);
            }
            catch
            {
                _buildId = -1;
                throw;
            }
        }

        /// <summary>
        /// Runs one cycle over what changed only (staging checkpoint 6, R9, R12): the foods and recipes the active build has
        /// no state for (a writer's trigger removed it, or they are new), the foods and recipes of the committed rows of the
        /// write log (<see cref="PendingWritesAsync"/>), and the recipes on those foods; it reads the catalog and the lines of
        /// those alone, decides each whose fingerprint moved or that has no state, and then removes the log rows it read. A
        /// write that commits while the cycle runs leaves its row for the next cycle (review CK11-DM-1), and a recipe with a
        /// row whose transaction the cycle's reading of the lines could not see, found by the statement that stores the
        /// states, is left undecided instead of stored from the older lines (<see cref="CycleReport.RecipesLate"/>). Falls back to
        /// <see cref="RunAsync"/> when the build or a set is not complete or a type's criteria changed.
        /// </summary>
        public async Task<CycleReport> RunChangesAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
        {
            try
            {
                return await ChangesAsync(db, cancellationToken);
            }
            catch
            {
                _buildId = -1;
                throw;
            }
        }

        private async Task<CycleReport> CycleAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            var writes = await ReadWritesAsync(db, cancellationToken);
            var version = _version();
            var build = await db.DietGateBuilds.Where(b => b.Version == version && b.State != DietGateStore.Retired).OrderByDescending(b => b.Id).FirstOrDefaultAsync(cancellationToken);
            if (build == null)
            {
                build = new DietGateBuildEntity { Version = version, State = DietGateStore.Building, StartedAt = DateTime.UtcNow };
                db.DietGateBuilds.Add(build);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (build.Id != _buildId) await LoadAsync(db, build.Id, cancellationToken);

            var specs = await SpecsAsync(db);
            await SyncSetsAsync(db, build, specs, cancellationToken);
            var sets = _sets.Values.OrderBy(s => s.Row.Id).ToList();
            var built = sets.Where(s => s.Building).ToList();

            var foodClock = Stopwatch.StartNew();
            var catalog = await DietFoodCatalog.LoadAsync(db, NutrientIds(specs), cancellationToken);
            var buildId = build.Id;
            var stored = (await db.DietFoodStates.AsNoTracking().Where(s => s.BuildId == buildId).Select(s => s.IngredientId).ToListAsync(cancellationToken)).ToHashSet();
            var changedFoods = catalog.Foods.Keys.Where(id => !stored.Contains(id) || !_foodPrints.TryGetValue(id, out var old) || old != catalog.Fingerprint(id)).ToHashSet();
            var goneFoods = _foodPrints.Keys.Where(id => !catalog.Foods.ContainsKey(id)).ToList();
            var factMoved = await DecideFoodsAsync(db, catalog, sets, changedFoods, goneFoods, cancellationToken);
            var moved = Moved(catalog, sets, changedFoods, goneFoods, factMoved);
            foreach (var id in catalog.Foods.Keys.Where(id => !changedFoods.Contains(id) && !_foodInputs.ContainsKey(id))) _foodInputs[id] = Inputs(catalog, sets, id);
            foodClock.Stop();

            var recipeClock = Stopwatch.StartNew();
            if (BeforeRecipes != null) await BeforeRecipes(cancellationToken);
            var mark = await MarkAsync(db, cancellationToken);
            var readers = ReadersOf(specs);
            var words = await WordsAsync(db, readers, specs, sets, cancellationToken);
            _words = words;
            var (live, changedRecipes, readings) = await ReadLinesAsync(db, buildId, readers, moved, built.Count > 0, words, catalog.Foods, cancellationToken);
            if (AfterLinesRead != null) await AfterLinesRead(cancellationToken);
            var goneRecipes = _recipePrints.Keys.Where(id => !live.Contains(id)).ToList();
            await RemoveRecipesAsync(db, buildId, goneRecipes, cancellationToken);
            foreach (var id in goneRecipes) _recipePrints.Remove(id);
            var (decided, late) = await DecideRecipesAsync(db, buildId, catalog, sets, live, changedRecipes, readings, mark, cancellationToken);
            await WriteFoodStatesAsync(db, buildId, catalog, changedFoods, goneFoods, cancellationToken);
            recipeClock.Stop();

            var now = DateTime.UtcNow;
            var reports = new List<DietGateSetReport>();
            foreach (var set in built)
            {
                set.Row.State = DietGateStore.Complete;
                set.Row.BuiltAt = now;
                set.Row.FoodSeconds = Math.Round(set.FoodClock.Elapsed.TotalSeconds, 3);
                set.Row.RecipeSeconds = Math.Round(set.RecipeClock.Elapsed.TotalSeconds, 3);
                set.Row.Foods = set.Facts.Values.Count(f => f.Verdict == DietVerdict.Contains);
                set.Row.RecipesContaining = set.Containing;
                set.Row.RecipesUnknown = set.Unknown;
                reports.Add(new DietGateSetReport(set.Row.Id, set.Row.RestrictionTypeId, string.Empty, set.Row.MinimumSeverity >= 4 ? "severe" : "all", set.Row.Severe, set.Row.State,
                    set.Row.BuiltAt, set.Row.FoodSeconds, set.Row.RecipeSeconds, set.Row.Foods, set.Row.RecipesContaining, set.Row.RecipesUnknown));
            }
            foreach (var set in sets)
            {
                set.FoodClock.Reset();
                set.RecipeClock.Reset();
            }

            var switched = false;
            if (build.State == DietGateStore.Building)
            {
                build.State = DietGateStore.Active;
                build.CompletedAt = now;
                build.FoodSeconds = Math.Round(foodClock.Elapsed.TotalSeconds, 3);
                build.RecipeSeconds = Math.Round(recipeClock.Elapsed.TotalSeconds, 3);
                build.TotalSeconds = Math.Round((now - build.StartedAt).TotalSeconds, 3);
                switched = true;
            }
            build.Foods = _foodPrints.Count;
            build.Recipes = _recipePrints.Count;
            build.Sets = sets.Count;
            build.LastCycleAt = now;
            build.LastCycleSeconds = Math.Round(clock.Elapsed.TotalSeconds, 3);
            build.LastCycleFoods = changedFoods.Count;
            build.LastCycleRecipes = decided.Count;
            build.Note = null;
            await db.SaveChangesAsync(cancellationToken);
            await RemoveSupersededAsync(db, buildId, cancellationToken);
            if (switched) await RetireAsync(db, buildId, cancellationToken);
            await ConsumeWritesAsync(db, writes, cancellationToken);

            return new CycleReport(buildId, build.Version, build.State, switched, sets.Count, changedFoods.Count, catalog.Foods.Count, decided.Count, live.Count,
                foodClock.Elapsed.TotalSeconds, recipeClock.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds, reports)
            {
                RecipesLate = late,
                FoodsMoved = moved.Count,
                RecipesConfirmed = writes.FoodRecipes.Count(id => live.Contains(id) && !changedRecipes.Contains(id)),
            };
        }

        private async Task<CycleReport> ChangesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var version = _version();
            var build = await db.DietGateBuilds.Where(b => b.Version == version && b.State != DietGateStore.Retired).OrderByDescending(b => b.Id).FirstOrDefaultAsync(cancellationToken);
            if (build == null || build.Id != _buildId || build.State != DietGateStore.Active || _sets.Values.Any(s => s.Building)) return await CycleAsync(db, cancellationToken);
            var specs = await SpecsAsync(db);
            if (!specs.Select(s => s.Key).ToHashSet(StringComparer.Ordinal).SetEquals(_sets.Values.Select(s => s.Row.CriteriaKey))) return await CycleAsync(db, cancellationToken);

            var clock = Stopwatch.StartNew();
            var writes = await ReadWritesAsync(db, cancellationToken);
            var sets = _sets.Values.OrderBy(s => s.Row.Id).ToList();
            var buildId = build.Id;

            var foodClock = Stopwatch.StartNew();
            var undecided = (await db.Ingredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => !db.DietFoodStates.Any(s => s.BuildId == buildId && s.IngredientId == i.Id))
                    .Select(i => i.Id)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var foodScope = undecided.Concat(writes.Foods).ToHashSet();
            var catalog = await DietFoodCatalog.LoadAsync(db, NutrientIds(specs), cancellationToken, foodScope);
            var changedFoods = foodScope.Where(id => catalog.Foods.ContainsKey(id)
                && (undecided.Contains(id) || !_foodPrints.TryGetValue(id, out var old) || old != catalog.Fingerprint(id))).ToHashSet();
            var goneFoods = foodScope.Where(id => !catalog.Foods.ContainsKey(id) && _foodPrints.ContainsKey(id)).ToList();
            var factMoved = await DecideFoodsAsync(db, catalog, sets, changedFoods, goneFoods, cancellationToken);
            var moved = Moved(catalog, sets, changedFoods, goneFoods, factMoved);
            foodClock.Stop();

            var recipeClock = Stopwatch.StartNew();
            if (BeforeRecipes != null) await BeforeRecipes(cancellationToken);
            var recipeScope = (await db.Recipes.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => !r.IsDeleted && !db.DietRecipeStates.Any(s => s.BuildId == buildId && s.RecipeId == r.Id))
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken))
                .Concat(writes.Recipes)
                .ToHashSet();
            foreach (var batch in moved.Chunk(5000))
                recipeScope.UnionWith(await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(ri => batch.Contains(ri.IngredientId))
                    .Select(ri => ri.RecipeId)
                    .Distinct()
                    .ToListAsync(cancellationToken));
            var windowed = recipeScope.Count > LargeScopeLimit;
            var mark = await MarkAsync(db, cancellationToken);
            var readers = ReadersOf(specs);
            var words = foodScope.Count == 0 && _words != null && _words.Rows.SequenceEqual(await CategoryRowsAsync(db, cancellationToken)) && _words.Clarifiers.SequenceEqual(await ClarifiersAsync(db, cancellationToken))
                    && _words.UnprovenRows.SequenceEqual(await UnprovenAsync(db, cancellationToken))
                ? _words
                : await WordsAsync(db, readers, specs, sets, cancellationToken);
            if (_words?.Signature != words.Signature) return await CycleAsync(db, cancellationToken);
            HashSet<long> live, changed;
            Dictionary<long, Reading> readings;
            DietFoodCatalog recipeCatalog;
            if (windowed)
            {
                recipeCatalog = await DietFoodCatalog.LoadAsync(db, NutrientIds(specs), cancellationToken);
                (live, changed, readings) = await ReadLinesAsync(db, buildId, readers, moved, false, words, recipeCatalog.Foods, cancellationToken);
            }
            else
            {
                var recipes = await RecipeLinesAsync(db, recipeScope, cancellationToken);
                recipeCatalog = await DietFoodCatalog.LoadAsync(db, NutrientIds(specs), cancellationToken,
                    recipes.Lines.Values.SelectMany(l => l.Select(x => x.IngredientId)).ToHashSet());
                var storedPrints = new Dictionary<long, long>();
                foreach (var batch in recipeScope.Chunk(5000))
                    foreach (var s in await db.DietRecipeStates.AsNoTracking().Where(s => s.BuildId == buildId && batch.Contains(s.RecipeId)).Select(s => new { s.RecipeId, s.Fingerprint }).ToListAsync(cancellationToken))
                        storedPrints[s.RecipeId] = s.Fingerprint;
                live = recipes.Servings.Keys.ToHashSet();
                (changed, readings) = ReadScope(readers, recipes, storedPrints, moved, words, recipeCatalog.Foods);
            }
            if (AfterLinesRead != null) await AfterLinesRead(cancellationToken);
            var gone = windowed
                ? _recipePrints.Keys.Where(id => !live.Contains(id)).ToList()
                : recipeScope.Where(id => !live.Contains(id) && _recipePrints.ContainsKey(id)).ToList();
            await RemoveRecipesAsync(db, buildId, gone, cancellationToken);
            foreach (var id in gone) _recipePrints.Remove(id);
            var (decided, late) = await DecideRecipesAsync(db, buildId, recipeCatalog, sets, live, changed, readings, mark, cancellationToken);
            await WriteFoodStatesAsync(db, buildId, catalog, changedFoods, goneFoods, cancellationToken);
            recipeClock.Stop();

            build.Foods = _foodPrints.Count;
            build.Recipes = _recipePrints.Count;
            build.LastCycleAt = DateTime.UtcNow;
            build.LastCycleSeconds = Math.Round(clock.Elapsed.TotalSeconds, 3);
            build.LastCycleFoods = changedFoods.Count;
            build.LastCycleRecipes = decided.Count;
            build.Note = null;
            await db.SaveChangesAsync(cancellationToken);
            await ConsumeWritesAsync(db, writes, cancellationToken);

            var goneSet = gone.ToHashSet();
            return new CycleReport(buildId, build.Version, build.State, false, sets.Count, changedFoods.Count, catalog.Foods.Keys.Union(recipeCatalog.Foods.Keys).Count(), decided.Count, _recipePrints.Count,
                foodClock.Elapsed.TotalSeconds, recipeClock.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds, Array.Empty<DietGateSetReport>())
            {
                RecipesLate = late,
                FoodsMoved = moved.Count,
                RecipesConfirmed = writes.FoodRecipes.Count(id => !changed.Contains(id) && !goneSet.Contains(id)),
                Windowed = windowed,
            };
        }

        /// <summary>
        /// Reads the recipes of a cycle over the changes on all cores, as the windowed reading does (staging checkpoint 7,
        /// S2: read one by one, a cycle over 116,639 recipes took 316 s where the whole cycle reads 122,103 in 67 s): a recipe
        /// is decided again when it has no stored state, its fingerprint moved, or one of its foods' decisions moved.
        /// </summary>
        private static (HashSet<long> Changed, Dictionary<long, Reading> Readings) ReadScope(Readers readers, RecipeLines recipes, Dictionary<long, long> storedPrints, HashSet<long> moved, Words words, IReadOnlyDictionary<long, DietFoodCatalog.Food> foods)
        {
            var ids = recipes.Servings.Keys.ToArray();
            var outcome = new Reading?[ids.Length];
            Parallel.For(0, ids.Length, i =>
            {
                var id = ids[i];
                var lines = recipes.Lines.TryGetValue(id, out var l) ? l : new List<LineRow>();
                var book = recipes.Books.GetValueOrDefault(id);
                var print = Print(lines, recipes.Servings[id], book, words.Signature);
                if (storedPrints.TryGetValue(id, out var was) && was == print && !lines.Any(x => moved.Contains(x.IngredientId))) return;
                outcome[i] = Read(readers, id, lines, print, book, words, foods);
            });
            var changed = new HashSet<long>();
            var readings = new Dictionary<long, Reading>();
            for (var i = 0; i < ids.Length; i++)
            {
                if (outcome[i] is not { } reading) continue;
                changed.Add(ids[i]);
                readings[ids[i]] = reading;
            }
            return (changed, readings);
        }

        /// <summary>
        /// The foods whose recipes must be decided again once the foods are: a food gone, a food decided for the first time,
        /// a food whose fact moved for some set, and a food whose retained alcohol inputs moved (the capped nutrient, a
        /// weight, its name, category or liveness). A write to a food that moves none of these (an alias no pattern reads, a
        /// category no criterion names, a nutrient below presence) changes no recipe's verdict, so its recipes are released
        /// as they were instead of read again (staging checkpoint 7, S2). Updates the fingerprints kept in memory.
        /// </summary>
        private HashSet<long> Moved(DietFoodCatalog catalog, List<SetState> sets, HashSet<long> changedFoods, List<long> goneFoods, HashSet<long> factMoved)
        {
            var moved = goneFoods.ToHashSet();
            foreach (var id in changedFoods)
            {
                var inputs = Inputs(catalog, sets, id);
                if (!_foodPrints.ContainsKey(id) || factMoved.Contains(id) || !_foodInputs.TryGetValue(id, out var was) || was != inputs) moved.Add(id);
                _foodPrints[id] = catalog.Fingerprint(id);
                _foodInputs[id] = inputs;
            }
            foreach (var id in goneFoods)
            {
                _foodPrints.Remove(id);
                _foodInputs.Remove(id);
            }
            return moved;
        }

        private static long Inputs(DietFoodCatalog catalog, List<SetState> sets, long id)
        {
            var food = catalog.Foods[id];
            var hash = new DietFoodCatalog.Fnv();
            hash.Add(food.Name).Add(food.Live ? "1" : "0").Add(food.Curated ? "curated" : "-").Add(food.CategoryId).Add(food.GramsPerMilliliter).Add(food.GramsPerItem).Add(food.ReferenceServingGrams);
            catalog.Nutrients.TryGetValue(id, out var nutrients);
            foreach (var nutrientId in sets.Where(s => s.Capped != null).Select(s => s.Capped!.NutrientId!.Value).Distinct().OrderBy(n => n))
                hash.Add(nutrientId).Add(nutrients != null && nutrients.TryGetValue(nutrientId, out var amount) ? (decimal?)amount : null);
            return hash.Value;
        }

        private static List<long> NutrientIds(List<Spec> specs) =>
            specs.SelectMany(s => s.Criteria).Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).Distinct().ToList();

        internal static Readers ReadersOf(List<Spec> specs)
        {
            var criteria = specs.SelectMany(s => s.Criteria).ToList();
            return new Readers(
                new DietLineRules.Matcher(DietLineRules.Rules(criteria)),
                new DietLineRules.Matcher(DietLineRules.Rules(criteria.Where(c => c.Rule == null || DietRecipeText.ReadsMethod(c.Rule)))),
                new DietLineRules.Matcher(DietLineRules.Rules(criteria.Where(c => c.Rule != null && DietRecipeText.Echoes(c.Rule)))),
                criteria.Where(c => c.Rule != null).GroupBy(c => DietLineRules.KeyOf(c)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Rule, StringComparer.Ordinal));
        }

        /// <summary>
        /// Every severe set's reading of words (<see cref="DietFoodWords"/>), from the live curated foods with their USDA
        /// descriptions and the set's decided facts, and the signature of them all: a change to it (a food curated or
        /// uncurated, a curated food's name, description or verdict) changes every recipe's fingerprint, so every recipe is
        /// decided again in a whole cycle. The category rows of the verified wording table (<see cref="WordingCategory"/>)
        /// are read with it: each set reads a group as flagged when every curated food of it is flagged, as clear when none is
        /// flagged or of unknown contents and its category is one of single commodities (<see cref="DietFoodWords.PlainCategories"/>),
        /// and as unknown otherwise or when no curated food is of it; the rows and their classes are part of the signature.
        /// </summary>
        internal static async Task<Words> WordsAsync(ApplicationDbContext db, Readers readers, List<Spec> specs, List<SetState> sets, CancellationToken cancellationToken)
        {
            var (table, hash) = await VocabularyAsync(db, readers, specs, sets, cancellationToken);
            var clarifiers = await ClarifiersAsync(db, cancellationToken);
            foreach (var set in sets)
            {
                set.ClarifiedBooks = !set.Spec.Severe
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : clarifiers.Where(c => readers.Lines.Read(new[] { new DietLineRules.Line(0, 0, 0, c.Clarifier) }, false).Rules.Any(set.LineKeys.ContainsKey)).Select(c => c.Book).ToHashSet(StringComparer.Ordinal);
                hash.Add("clarified").Add(set.Row.CriteriaKey);
                foreach (var book in set.ClarifiedBooks.OrderBy(b => b, StringComparer.Ordinal)) hash.Add(book);
            }
            var unproven = await UnprovenAsync(db, cancellationToken);
            hash.Add("unproven");
            foreach (var (food, wording) in unproven) hash.Add(food).Add(wording);
            return Grouped(table, hash, sets, await CategoryRowsAsync(db, cancellationToken)) with { Clarifiers = clarifiers, UnprovenRows = unproven, Unproven = UnprovenOf(unproven) };
        }

        /// <summary>
        /// The links an unproven lane made, by food and wording: the applied model picks (<c>ai:</c> link proposals, the lane
        /// the re-check has not proven, staging checkpoint 7's draws: "Protose" on Fresh Coconut Meat), less the wordings a
        /// verified wording row gives that same food. A line on such a link whose text names a word no curated food knows
        /// (a brand or trade name no rule knows) is read by its words, not as the food its link sits on.
        /// </summary>
        internal static async Task<IReadOnlyList<(long Food, string Wording)>> UnprovenAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var picks = await db.FoodCatalogProposals.AsNoTracking()
                .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == Nom.Data.Curation.FoodProposalStatus.Applied && p.Source.StartsWith("ai:")
                    && p.ProposedValue != null && p.CurrentValue != null)
                .Select(p => new { p.CurrentValue, p.ProposedValue })
                .ToListAsync(cancellationToken);
            var verified = (await db.IngredientWordings.AsNoTracking()
                    .Where(w => w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                    .Select(w => new { w.Wording, w.IngredientId })
                    .ToListAsync(cancellationToken))
                .Select(w => (w.IngredientId!.Value, IngredientNameNormalizer.WordingKey(w.Wording)))
                .ToHashSet();
            var result = new HashSet<(long, string)>();
            foreach (var p in picks)
            {
                if (!long.TryParse(p.ProposedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var food)) continue;
                var wording = IngredientNameNormalizer.WordingKey(p.CurrentValue!);
                if (wording.Length > 0 && !verified.Contains((food, wording))) result.Add((food, wording));
            }
            return result.OrderBy(x => x.Item1).ThenBy(x => x.Item2, StringComparer.Ordinal).ToList();
        }

        internal static IReadOnlyDictionary<long, UnprovenLinks> UnprovenOf(IEnumerable<(long Food, string Wording)> rows) =>
            rows.GroupBy(r => r.Food).ToDictionary(g => g.Key, g => new UnprovenLinks(
                g.Select(r => r.Wording).ToHashSet(StringComparer.OrdinalIgnoreCase),
                g.SelectMany(r => IngredientNameNormalizer.ExactKeys(r.Wording)).ToHashSet(StringComparer.OrdinalIgnoreCase)));

        /// <summary>Whether a line sits on its food by an unproven link: its wording, as the verified relink reads it (<see cref="LineRelinkService.NamesWording"/>), is one an unproven lane linked to that food.</summary>
        internal static bool OnUnprovenLink(string? rawLine, UnprovenLinks links) =>
            IngredientNameNormalizer.ExactKeys(rawLine ?? string.Empty).Any(links.Wordings.Contains)
            || links.Keys.Contains(IngredientNameNormalizer.WordingKey(rawLine ?? string.Empty));

        /// <summary>
        /// The egg-white clarifiers of each book's own consommé recipes (<see cref="DietRecipeText.ConsommeClarifiers"/>), by
        /// the book's attribution, in order: a book recipe titled for a consommé whose lines or method name egg white (129365
        /// Escoffier's THE PREPARATION OF CLARIFIED CONSOMMÉ, "the white of an egg") says how that book makes the consommé its
        /// other recipes' lines name, whatever food those lines are linked to (beef consommé, broth or stock).
        /// </summary>
        internal static async Task<IReadOnlyList<(string Book, string Clarifier)>> ClarifiersAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var titled = (await db.Recipes.AsNoTracking()
                    .Where(r => r.ScrapedAtUtc != null && r.SourceAttribution != null && r.Name.ToLower().Contains("consomm"))
                    .Select(r => new { r.Id, r.Name, r.SourceUrl, r.ScrapedAtUtc, r.SourceAttribution })
                    .ToListAsync(cancellationToken))
                .Where(r => DietRecipeText.FromBook(r.SourceUrl, r.ScrapedAtUtc) && DietRecipeText.NamesConsomme(r.Name))
                .ToList();
            if (titled.Count == 0) return Array.Empty<(string, string)>();
            var ids = titled.Select(r => r.Id).ToList();
            var lines = (await db.RecipeIngredients.AsNoTracking().Where(ri => ids.Contains(ri.RecipeId)).Select(ri => new { ri.RecipeId, ri.RawLine }).ToListAsync(cancellationToken))
                .GroupBy(l => l.RecipeId).ToDictionary(g => g.Key, g => g.Select(l => l.RawLine).ToList());
            var methods = await MethodsAsync(db, ids, cancellationToken);
            return titled
                .SelectMany(r => DietRecipeText.ConsommeClarifiers(r.Name, lines.GetValueOrDefault(r.Id) ?? new List<string?>(), methods.GetValueOrDefault(r.Id)).Select(c => (Book: r.SourceAttribution!, Clarifier: c)))
                .Distinct()
                .OrderBy(c => c.Book, StringComparer.Ordinal).ThenBy(c => c.Clarifier, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The category rows of the verified wording table, in wording order: a changes cycle that finds no food written keeps its words only while these are the same.</summary>
        internal static async Task<IReadOnlyList<CategoryWording>> CategoryRowsAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
            (await db.IngredientWordings.AsNoTracking()
                    .Where(w => w.Verdict == IngredientWordingVerdict.Category && w.FdcCategoryId != null)
                    .Select(w => new { w.Wording, CategoryId = w.FdcCategoryId!.Value, w.CategoryHead })
                    .ToListAsync(cancellationToken))
                .Select(w => new CategoryWording(w.Wording, w.CategoryId, w.CategoryHead))
                .OrderBy(w => w.Wording, StringComparer.Ordinal)
                .ToList();

        /// <summary>The table of a cycle's curated foods and every severe set's vocabulary of it, with the signature so far; <see cref="WordsAsync"/> adds the category rows. A food flagged only by its brands' consensus (<see cref="BrandedConsensus.FactSource"/>) is no knowledge of its words: "soup" in an old cookbook's "soup stock" is not the condensed soup a can holds.</summary>
        internal static async Task<(DietFoodWords.Table Table, DietFoodCatalog.Fnv Hash)> VocabularyAsync(ApplicationDbContext db, Readers readers, List<Spec> specs, List<SetState> sets, CancellationToken cancellationToken)
        {
            var rows = await db.Ingredients.AsNoTracking()
                .Where(i => i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .Select(i => new { i.Id, i.Name, i.FdcCategoryId })
                .ToListAsync(cancellationToken);
            var descriptions = new Dictionary<long, string>();
            var aliases = new Dictionary<long, List<string>>();
            foreach (var batch in rows.Select(r => r.Id).Chunk(5000))
                foreach (var a in (await db.IngredientAliases.AsNoTracking()
                             .Where(a => batch.Contains(a.IngredientId))
                             .Where(IngredientAliasTrust.IsTrustedAlias)
                             .Select(a => new { a.Id, a.IngredientId, a.AliasName, a.SourceContext })
                             .ToListAsync(cancellationToken)).OrderBy(a => a.Id))
                {
                    if (a.SourceContext == IngredientLinkService.UsdaDescriptionContext && descriptions.TryAdd(a.IngredientId, a.AliasName ?? string.Empty)) continue;
                    if (!aliases.TryGetValue(a.IngredientId, out var list)) aliases[a.IngredientId] = list = new List<string>();
                    list.Add(DietMeasureWords.Blank(a.AliasName ?? string.Empty));
                }
            var table = DietFoodWords.TableOf(rows.Select(r => new DietFoodWords.Food(r.Id, r.Name ?? string.Empty, r.FdcCategoryId, descriptions.GetValueOrDefault(r.Id), aliases.GetValueOrDefault(r.Id))));
            var named = table.Foods.Select(f => readers.Lines.Read(new[] { f.Name }.Concat(f.Description is { } d ? new[] { d } : Array.Empty<string>()).Concat(f.Aliases ?? Array.Empty<string>())
                .Select(t => new DietLineRules.Line(f.Id, f.Id, 0, t)).ToList(), false).Rules).ToArray();
            var termWords = specs.SelectMany(s => s.Criteria)
                .Select(c => (Key: DietLineRules.KeyOf(c), c))
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)DietFoodWords.TermWords(g.First().c), StringComparer.Ordinal);
            var hash = new DietFoodCatalog.Fnv();
            hash.Add(DietFoodWords.Version);
            foreach (var set in sets.OrderBy(s => s.Row.CriteriaKey, StringComparer.Ordinal))
            {
                if (!set.Spec.Severe)
                {
                    set.Vocabulary = null;
                    continue;
                }
                var keys = set.AllKeys;
                var oneWord = set.LineKeys.Keys.Select(k => termWords[k]).Where(w => w.Count == 1).Select(w => w[0]).ToHashSet(StringComparer.Ordinal);
                var kinds = DietFoodWords.KindsOf(set.Spec.Criteria.Where(c => c.LikePattern != null).Select(c => c.LikePattern!));
                set.Vocabulary = DietFoodWords.Build(table,
                    i => set.Facts.TryGetValue(table.Foods[i].Id, out var fact) && fact.Verdict == DietVerdict.Contains && fact.Source != BrandedConsensus.FactSource,
                    i => named[i].Where(keys.ContainsKey).Select(k => termWords[k]).ToList(),
                    oneWord, kinds);
                hash.Add(set.Row.CriteriaKey);
                set.Vocabulary.AddTo(hash, table);
            }
            return (table, hash);
        }

        /// <summary>A category row of the verified wording table as the reading takes it.</summary>
        internal sealed record CategoryWording(string Wording, int CategoryId, string? Head);

        /// <summary>The words of a cycle with the category rows read into them: each severe set's class of every group, and the rows and classes added to the signature.</summary>
        internal static Words Grouped(DietFoodWords.Table table, DietFoodCatalog.Fnv hash, List<SetState> sets, IReadOnlyList<CategoryWording> rows)
        {
            var ordered = rows.OrderBy(w => w.Wording, StringComparer.Ordinal).ToList();
            var groupKeys = ordered.Select(w => (w.CategoryId, Head: WordingCategory.Head(w.Head)?.ToLowerInvariant())).Distinct().ToList();
            var groupMembers = groupKeys.Select(g => Enumerable.Range(0, table.Foods.Count)
                .Where(i => WordingCategory.Holds(g.CategoryId, g.Head, table.Foods[i].CategoryId, table.Foods[i].Description, table.Foods[i].Name)).ToArray()).ToList();
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in ordered) groups[row.Wording] = groupKeys.IndexOf((row.CategoryId, WordingCategory.Head(row.Head)?.ToLowerInvariant()));
            foreach (var row in ordered) hash.Add("group").Add(row.Wording).Add(groups[row.Wording]);
            foreach (var set in sets.OrderBy(s => s.Row.CriteriaKey, StringComparer.Ordinal))
            {
                if (set.Vocabulary == null)
                {
                    set.GroupClasses = Array.Empty<DietFoodWords.WordClass>();
                    continue;
                }
                set.GroupClasses = groupKeys.Select((g, i) => GroupClass(set, table, g.CategoryId, groupMembers[i])).ToArray();
                hash.Add(set.Row.CriteriaKey);
                foreach (var c in set.GroupClasses) hash.Add((long)c);
            }
            return new Words(table, hash.Value) { Groups = groups, Rows = ordered };
        }

        private static DietFoodWords.WordClass GroupClass(SetState set, DietFoodWords.Table table, int categoryId, int[] members)
        {
            if (members.Length == 0) return DietFoodWords.WordClass.NotKnown;
            var flagged = 0;
            var open = 0;
            foreach (var i in members)
            {
                if (!set.Facts.TryGetValue(table.Foods[i].Id, out var fact)) continue;
                if (fact.Verdict == DietVerdict.Contains && fact.Source != BrandedConsensus.FactSource) flagged++;
                else open++;
            }
            if (flagged == members.Length) return DietFoodWords.WordClass.Flagged;
            return flagged == 0 && open == 0 && DietFoodWords.PlainCategories.Contains(categoryId) ? DietFoodWords.WordClass.Clear : DietFoodWords.WordClass.Both;
        }

        /// <summary>Records a failed cycle on the build being worked on; the build keeps what it had, and requests keep reading the active build.</summary>
        public static async Task NoteFailureAsync(ApplicationDbContext db, string note, CancellationToken cancellationToken = default)
        {
            var version = DietGateStore.Version;
            var build = await db.DietGateBuilds.Where(b => b.Version == version && b.State != DietGateStore.Retired).OrderByDescending(b => b.Id).FirstOrDefaultAsync(cancellationToken);
            if (build == null) return;
            build.Note = note.Length > 2047 ? note[..2047] : note;
            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task LoadAsync(ApplicationDbContext db, long buildId, CancellationToken cancellationToken)
        {
            _buildId = buildId;
            _foodPrints.Clear();
            _foodInputs.Clear();
            _recipePrints.Clear();
            _sets.Clear();
            _words = null;
            foreach (var s in await db.DietFoodStates.AsNoTracking().Where(s => s.BuildId == buildId).Select(s => new { s.IngredientId, s.Fingerprint }).ToListAsync(cancellationToken))
                _foodPrints[s.IngredientId] = s.Fingerprint;
            foreach (var s in await db.DietRecipeStates.AsNoTracking().Where(s => s.BuildId == buildId).Select(s => new { s.RecipeId, s.Fingerprint }).ToListAsync(cancellationToken))
                _recipePrints[s.RecipeId] = s.Fingerprint;
            foreach (var row in await db.DietGateSets.AsNoTracking().Where(s => s.BuildId == buildId && s.State == DietGateStore.Complete).ToListAsync(cancellationToken))
            {
                var set = new SetState(row);
                foreach (var f in await db.DietFoodFacts.AsNoTracking().Where(f => f.SetId == row.Id).ToListAsync(cancellationToken))
                    set.Facts[f.IngredientId] = new Fact(f.Verdict, f.CriterionId, f.CriterionIds, f.Source);
                _sets[row.Id] = set;
            }
        }

        internal static async Task<List<Spec>> SpecsAsync(ApplicationDbContext db)
        {
            var typeIds = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Where(c => c.NutrientId != null || c.IngredientPattern != null)
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync();
            var criteria = await DietCriteriaMatcher.CriteriaAsync(db, typeIds);
            var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId).OrderBy(g => g.Key))
            {
                foreach (var (minimum, list) in new[] { (0, type.ToList()), (4, type.Where(c => c.Severity >= 4).ToList()) })
                {
                    if (list.Count == 0) continue;
                    var key = DietGateStore.KeyOf(list);
                    if (!specs.ContainsKey(key)) specs[key] = new Spec(type.Key, minimum, key, DietGateStore.CriteriaText(list), list.Any(c => c.Severity >= 4), list);
                }
            }
            return specs.Values.ToList();
        }

        private async Task SyncSetsAsync(ApplicationDbContext db, DietGateBuildEntity build, List<Spec> specs, CancellationToken cancellationToken)
        {
            var rows = await db.DietGateSets.Where(s => s.BuildId == build.Id).ToListAsync(cancellationToken);
            var wanted = specs.ToDictionary(s => s.Key, StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!wanted.ContainsKey(row.CriteriaKey))
                {
                    _sets.Remove(row.Id);
                    if (build.State == DietGateStore.Active && row.State != DietGateStore.Building)
                    {
                        row.State = DietGateStore.Superseded;
                        continue;
                    }
                    await DeleteSetRowsAsync(db, row.Id, cancellationToken);
                    db.DietGateSets.Remove(row);
                }
                else if (row.State == DietGateStore.Building || row.State == DietGateStore.Superseded)
                {
                    row.State = DietGateStore.Building;
                    await DeleteSetRowsAsync(db, row.Id, cancellationToken);
                    _sets[row.Id] = new SetState(row);
                }
                else if (!_sets.ContainsKey(row.Id))
                {
                    var set = new SetState(row);
                    foreach (var f in await db.DietFoodFacts.AsNoTracking().Where(f => f.SetId == row.Id).ToListAsync(cancellationToken))
                        set.Facts[f.IngredientId] = new Fact(f.Verdict, f.CriterionId, f.CriterionIds, f.Source);
                    _sets[row.Id] = set;
                }
            }
            var have = rows.Select(r => r.CriteriaKey).ToHashSet(StringComparer.Ordinal);
            var added = specs.Where(s => !have.Contains(s.Key))
                .Select(s => new DietGateSetEntity { BuildId = build.Id, CriteriaKey = s.Key, RestrictionTypeId = s.TypeId, MinimumSeverity = s.MinimumSeverity, Severe = s.Severe, Criteria = s.Text, State = DietGateStore.Building })
                .ToList();
            db.DietGateSets.AddRange(added);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var row in added) _sets[row.Id] = new SetState(row);
            foreach (var set in _sets.Values) Configure(set, wanted[set.Row.CriteriaKey]);
        }

        internal static void Configure(SetState set, Spec spec)
        {
            set.Spec = spec;
            set.LineKeys = DietCriteriaMatcher.LinePatterns(spec.Criteria)
                .Select(c => (Key: DietLineRules.KeyOf(c), c))
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Top(g.Select(x => x.c)).Id, StringComparer.Ordinal);
            set.AllKeys = spec.Criteria
                .Select(c => (Key: DietLineRules.KeyOf(c), c))
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Top(g.Select(x => x.c)).Id, StringComparer.Ordinal);
            set.DrinkCriterion = DietCriteriaMatcher.ReadsLines(spec.Criteria)
                ? Top(spec.Criteria.Where(c => c.NutrientId != null && c.MaxAmountPerServing == null && DietCriteriaMatcher.IsAlcohol(c.NutrientName))).Id
                : null;
            set.Capped = DietCriteriaMatcher.RetainedCriterion(spec.Criteria);
        }

        private static DietDataCriterion Top(IEnumerable<DietDataCriterion> criteria) => criteria.OrderByDescending(c => c.Severity).ThenBy(c => c.Id).First();

        private async Task<HashSet<long>> DecideFoodsAsync(ApplicationDbContext db, DietFoodCatalog catalog, List<SetState> sets, HashSet<long> changedFoods, List<long> goneFoods, CancellationToken cancellationToken)
        {
            var factMoved = new HashSet<long>();
            var anyBuilding = sets.Any(s => s.Building);
            var subset = anyBuilding ? catalog.Foods.Keys.ToHashSet() : changedFoods;
            var deletes = new List<(long SetId, long FoodId)>();
            var inserts = new List<DietFoodFactEntity>();
            if (subset.Count > 0)
            {
                await catalog.LoadStatementsAsync(db, subset, cancellationToken);
                var reading = DietFoodReader.Read(catalog, subset);
                foreach (var set in sets)
                {
                    if (!set.Building && changedFoods.Count == 0) continue;
                    set.FoodClock.Start();
                    var decision = DietFoodReader.Decide(reading, set.Spec.Criteria);
                    var facts = Facts(decision, set.Spec.Criteria);
                    var scope = set.Building ? (IEnumerable<long>)subset : changedFoods;
                    foreach (var food in scope)
                    {
                        set.Facts.TryGetValue(food, out var old);
                        facts.TryGetValue(food, out var fresh);
                        if (old == fresh) continue;
                        if (!set.Building) factMoved.Add(food);
                        if (old != null)
                        {
                            deletes.Add((set.Row.Id, food));
                            set.Facts.Remove(food);
                        }
                        if (fresh != null)
                        {
                            inserts.Add(new DietFoodFactEntity { SetId = set.Row.Id, IngredientId = food, Verdict = fresh.Verdict, CriterionId = fresh.CriterionId, CriterionIds = fresh.CriterionIds, Source = fresh.Source });
                            set.Facts[food] = fresh;
                        }
                    }
                    set.FoodClock.Stop();
                }
            }
            foreach (var set in sets)
                foreach (var food in goneFoods)
                    if (set.Facts.Remove(food)) deletes.Add((set.Row.Id, food));

            await InTransactionAsync(db, async () =>
            {
                foreach (var group in deletes.GroupBy(d => d.SetId))
                    foreach (var batch in group.Select(d => d.FoodId).Chunk(5000))
                        await DeleteAsync(db, db.DietFoodFacts.Where(f => f.SetId == group.Key && batch.Contains(f.IngredientId)), cancellationToken);
                await InsertFactsAsync(db, inserts, cancellationToken);
            }, cancellationToken);
            return factMoved;
        }

        /// <summary>
        /// Stores the fingerprints of the foods decided in this cycle, only once the recipes on them are decided again
        /// (review CK10S-1): a cycle that fails between the two leaves the old fingerprints, so the next cycle sees those
        /// foods as changed and decides their recipes again.
        /// </summary>
        private static async Task WriteFoodStatesAsync(ApplicationDbContext db, long buildId, DietFoodCatalog catalog, HashSet<long> changedFoods, List<long> goneFoods, CancellationToken cancellationToken)
        {
            if (changedFoods.Count == 0 && goneFoods.Count == 0) return;
            var states = changedFoods.Select(id => new DietFoodStateEntity { BuildId = buildId, IngredientId = id, Fingerprint = catalog.Fingerprint(id), ComputedAt = DateTime.UtcNow }).ToList();
            await InTransactionAsync(db, async () =>
            {
                foreach (var batch in changedFoods.Concat(goneFoods).Chunk(5000))
                    await DeleteAsync(db, db.DietFoodStates.Where(s => s.BuildId == buildId && batch.Contains(s.IngredientId)), cancellationToken);
                await InsertFoodStatesAsync(db, states, cancellationToken);
            }, cancellationToken);
        }

        private static Dictionary<long, Fact> Facts(DietFoodReader.Decision decision, IReadOnlyList<DietDataCriterion> criteria)
        {
            var byId = criteria.ToDictionary(c => c.Id);
            var result = new Dictionary<long, Fact>();
            foreach (var food in decision.Foods.SelectMany(kv => kv.Value).Distinct())
            {
                var counting = decision.Foods.Where(kv => kv.Value.Contains(food) && byId.ContainsKey(kv.Key)).Select(kv => byId[kv.Key]).ToList();
                var top = Top(counting);
                result[food] = new Fact(DietVerdict.Contains, top.Id, string.Join(',', counting.Select(c => c.Id).OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture))),
                    decision.Sources.GetValueOrDefault((top.Id, food)));
            }
            foreach (var food in decision.AliasOnly.Where(f => !result.ContainsKey(f)))
                result[food] = new Fact(DietVerdict.AliasOnly, null, null, "alias");
            foreach (var food in decision.Unknown.Where(f => !result.ContainsKey(f)))
                result[food] = new Fact(DietVerdict.Unknown, null, null, "contents-unknown");
            foreach (var food in decision.Split.Where(f => !result.ContainsKey(f)))
                result[food] = new Fact(DietVerdict.Unknown, null, null, BrandedConsensus.SplitFactSource);
            return result;
        }

        internal static long Print(IReadOnlyList<LineRow> lines, string servings, BookText? book, long words)
        {
            var hash = new DietFoodCatalog.Fnv();
            foreach (var line in lines)
                hash.Add(line.IngredientId).Add(line.SortOrder).Add(line.RawLine).Add(line.Quantity).Add(line.MeasurementId).Add(line.LineKind).Add(line.AlcoholRetention).Add(line.AlcoholRetentionBasis);
            hash.Add("|").Add(servings);
            hash.Add("|").Add(book?.Name).Add(book?.Method).Add(book?.Book);
            hash.Add("|").Add(words);
            return hash.Value;
        }

        private static readonly HashSet<string> NoFoodKinds = new(StringComparer.Ordinal) { RecipeIngredientLineKind.Equipment, RecipeIngredientLineKind.Note, RecipeIngredientLineKind.Instruction };

        private static LineWords[] WordsOf(IReadOnlyList<LineRow> lines, Words words, IReadOnlyDictionary<long, DietFoodCatalog.Food> foods)
        {
            var result = new LineWords[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var food = foods.GetValueOrDefault(line.IngredientId);
                if ((line.LineKind != null && NoFoodKinds.Contains(line.LineKind)) || DietRecipeText.IsPlaceholder(line.RawLine))
                {
                    result[i] = new LineWords(line.IngredientId, Array.Empty<int>(), false);
                    continue;
                }
                var text = string.IsNullOrWhiteSpace(line.RawLine) ? food?.Name : DietFoodWords.Respell(line.RawLine);
                var read = food is not { Curated: true, Live: true };
                var known = words.Table.Read(text);
                if (!read && known.Length > 0 && known.All(w => w < 0) && words.Unproven.TryGetValue(line.IngredientId, out var links) && OnUnprovenLink(line.RawLine, links)) read = true;
                result[i] = new LineWords(line.IngredientId, known, read, read ? GroupOf(line, food, words) : -1, line.LineKind == RecipeIngredientLineKind.Reference, DietRecipeText.LeavesChoice(line.RawLine) || (read && LinkRecheckRules.HasFreeMarker(line.RawLine)));
            }
            return result;
        }

        /// <summary>
        /// The group of the category row a line's wording has (<see cref="WordingCategory"/>): the line is on an uncurated row
        /// whose name, lower-cased, is the wording, as the verified relink finds its sources, and its own text names the wording
        /// as the relink reads it, so a stray line an import parked on the row ("one gallon of water" on isinglass) is still
        /// read by its words. -1 for none.
        /// </summary>
        private static int GroupOf(LineRow line, DietFoodCatalog.Food? food, Words words)
        {
            if (words.Groups.Count == 0 || food == null || (line.LineKind != null && line.LineKind != RecipeIngredientLineKind.Ingredient)) return -1;
            var name = food.Name.ToLowerInvariant();
            if (!words.Groups.TryGetValue(name, out var group)) return -1;
            return LineRelinkService.NamesWording(string.IsNullOrWhiteSpace(line.RawLine) ? food.Name : line.RawLine, name) ? group : -1;
        }

        internal static Reading Read(Readers readers, long id, IReadOnlyList<LineRow> lines, long print, BookText? book, Words words, IReadOnlyDictionary<long, DietFoodCatalog.Food> foods)
        {
            var (matched, drinks) = readers.Lines.Read(lines.Select(x => new DietLineRules.Line(x.RecipeId, x.IngredientId, x.SortOrder, DietFoodWords.Respell(x.RawLine))).ToList(), true);
            var foodIds = lines.Select(x => x.IngredientId).Distinct().ToArray();
            var lineWords = WordsOf(lines, words, foods);
            if (book == null)
                return new Reading(print, matched, drinks, foodIds, lines.Count(x => !DietRecipeText.IsPlaceholder(x.RawLine) && (x.LineKind == null || !NoFoodKinds.Contains(x.LineKind))), Array.Empty<string>(), Array.Empty<string>()) { Words = lineWords, Table = words.Table };
            var method = DietFoodWords.Respell(book.Method);
            var raws = lines.Select(x => DietFoodWords.Respell(x.RawLine)).ToList();
            var elsewhere = DietRecipeText.RefersElsewhere(raws, method);
            var titleRules = readers.Lines.Read(new[] { new DietLineRules.Line(id, 0, 0, DietRecipeText.Title(DietFoodWords.Respell(book.Name))) }, false).Rules
                .Where(k => DietRecipeText.ReadsTitle(method, readers.RuleOf.GetValueOrDefault(k), elsewhere, book.Name, raws)).ToArray();
            var sentences = DietRecipeText.Sentences(method, book.Name, raws).Select(t => new DietLineRules.Line(id, 0, 0, t)).ToList();
            var methodRules = sentences.Count == 0 ? Array.Empty<string>() : readers.Method.Read(sentences, false).Rules
                .Concat(readers.Echo.Read(sentences, false).Rules.Where(k => readers.RuleOf.GetValueOrDefault(k) is { } echo && DietRecipeText.ReadsMethod(echo, raws)))
                .Concat(readers.Lines.Read(sentences.SelectMany(t => DietRecipeText.Mentions(t.RawLine!, raws)).Select(t => new DietLineRules.Line(id, 0, 0, t)).ToList(), false).Rules)
                .Concat(readers.Method.Read(sentences.SelectMany(t => DietRecipeText.Contractions(t.RawLine)).Select(t => new DietLineRules.Line(id, 0, 0, t)).ToList(), false).Rules)
                .Distinct(StringComparer.Ordinal).ToArray();
            var carried = lines.Where(x => x.LineKind == null || !NoFoodKinds.Contains(x.LineKind)).SelectMany(x => DietFoodWords.AllWords(x.RawLine).Concat(DietFoodWords.AllWords(foods.GetValueOrDefault(x.IngredientId)?.Name))).ToHashSet(StringComparer.Ordinal);
            var methodNamed = sentences.SelectMany(t => DietFoodWords.MethodPairs(t.RawLine, carried)).Distinct().ToList();
            var methodWords = methodNamed.Select(p => (Word: words.Table.IndexOf(p.Word), Next: p.Next == null ? -1 : words.Table.IndexOf(p.Next))).Where(p => p.Word >= 0).Distinct().ToArray();
            var titled = DietFoodWords.AllWords(DietRecipeText.Title(DietFoodWords.Respell(book.Name))).ToHashSet(StringComparer.Ordinal);
            var methodFood = methodNamed.Select(p => p.Word).FirstOrDefault(w => words.Table.IndexOf(w) < 0 && (DietFoodWords.IsMethodFood(w) || (DietFoodWords.IsMethodDish(w) && !titled.Contains(w))));
            return new Reading(print, matched, drinks, foodIds, DietRecipeText.BuildsOnAnother(method, raws) ? 0 : lines.Count(x => !DietRecipeText.IsPlaceholder(x.RawLine) && (x.LineKind == null || !NoFoodKinds.Contains(x.LineKind))), titleRules, methodRules)
            {
                Words = lineWords,
                Table = words.Table,
                MethodWords = methodWords,
                MethodFood = methodFood,
                Book = book.Book,
                ConsommeLine = lines.FirstOrDefault(x => (x.LineKind == null || !NoFoodKinds.Contains(x.LineKind)) && DietRecipeText.NamesConsomme(x.RawLine))?.IngredientId,
                DoughLost = DietRecipeText.DoughLost(DietFoodWords.Respell(book.Name), raws) || DietRecipeText.PasteLost(method, raws),
                FlourLost = DietRecipeText.LeaveningLost(raws),
            };
        }

        private static async Task<Dictionary<long, string>> MethodsAsync(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken) =>
            recipeIds.Count == 0 ? new Dictionary<long, string>() : (await db.RecipeSteps.IgnoreQueryFilters().AsNoTracking()
                    .Where(s => !s.IsDeleted && recipeIds.Contains(s.RecipeId))
                    .Select(s => new { s.RecipeId, s.StepNumber, s.Description })
                    .ToListAsync(cancellationToken))
                .GroupBy(s => s.RecipeId)
                .ToDictionary(g => g.Key, g => string.Join("\n", g.OrderBy(s => s.StepNumber).Select(s => s.Description)));

        private static string ServingsOf(long? servings, decimal? recipeServings) =>
            servings?.ToString(CultureInfo.InvariantCulture) + "/" + recipeServings?.ToString(CultureInfo.InvariantCulture);

        private static async Task<RecipeLines> RecipeLinesAsync(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken)
        {
            var servings = new Dictionary<long, string>();
            var lines = new Dictionary<long, List<LineRow>>();
            var books = new Dictionary<long, BookText>();
            foreach (var batch in recipeIds.Distinct().Chunk(5000))
            {
                var names = new Dictionary<long, (string? Name, string? Book)>();
                foreach (var r in await db.Recipes.IgnoreQueryFilters().AsNoTracking()
                             .Where(r => !r.IsDeleted && batch.Contains(r.Id))
                             .Select(r => new { r.Id, r.Servings, r.RecipeServings, r.Name, r.SourceUrl, r.ScrapedAtUtc, r.SourceAttribution })
                             .ToListAsync(cancellationToken))
                {
                    servings[r.Id] = ServingsOf(r.Servings, r.RecipeServings);
                    if (DietRecipeText.FromBook(r.SourceUrl, r.ScrapedAtUtc)) names[r.Id] = (r.Name, r.SourceAttribution);
                }
                var methods = await MethodsAsync(db, names.Keys.ToList(), cancellationToken);
                foreach (var (id, (name, attribution)) in names) books[id] = new BookText(name, methods.GetValueOrDefault(id), attribution);
                var rows = await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(ri => !ri.IsDeleted && batch.Contains(ri.RecipeId))
                    .Select(ri => new LineRow(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis))
                    .ToListAsync(cancellationToken);
                foreach (var g in rows.GroupBy(l => l.RecipeId))
                    lines[g.Key] = g.OrderBy(l => l.SortOrder).ThenBy(l => l.IngredientId).ToList();
            }
            return new RecipeLines(servings, lines, books);
        }

        private static async Task<(HashSet<long> Live, HashSet<long> Changed, Dictionary<long, Reading> Readings)> ReadLinesAsync(
            ApplicationDbContext db, long buildId, Readers readers, HashSet<long> movedFoods, bool all, Words words, IReadOnlyDictionary<long, DietFoodCatalog.Food> foods, CancellationToken cancellationToken)
        {
            var recipeRows = (await db.Recipes.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => !r.IsDeleted)
                    .Select(r => new { r.Id, r.Servings, r.RecipeServings, r.Name, r.SourceUrl, r.ScrapedAtUtc, r.SourceAttribution })
                    .ToListAsync(cancellationToken))
                .ToDictionary(r => r.Id);
            var servings = recipeRows.ToDictionary(r => r.Key, r => ServingsOf(r.Value.Servings, r.Value.RecipeServings));
            var live = servings.Keys.ToHashSet();
            var changed = new HashSet<long>();
            var readings = new Dictionary<long, Reading>();
            var max = live.Count == 0 ? 0 : live.Max();
            for (var after = 0L; after < max; after += LineWindow)
            {
                var upTo = Math.Min(after + LineWindow, max);
                var lines = await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(ri => !ri.IsDeleted && ri.RecipeId > after && ri.RecipeId <= upTo)
                    .Select(ri => new LineRow(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis))
                    .ToListAsync(cancellationToken);
                var byRecipe = lines.GroupBy(l => l.RecipeId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.IngredientId).ToList());
                var methods = (await db.RecipeSteps.IgnoreQueryFilters().AsNoTracking()
                        .Where(s => !s.IsDeleted && s.RecipeId > after && s.RecipeId <= upTo)
                        .Select(s => new { s.RecipeId, s.StepNumber, s.Description })
                        .ToListAsync(cancellationToken))
                    .GroupBy(s => s.RecipeId)
                    .ToDictionary(g => g.Key, g => string.Join("\n", g.OrderBy(s => s.StepNumber).Select(s => s.Description)));
                var stored = await db.DietRecipeStates.AsNoTracking()
                    .Where(s => s.BuildId == buildId && s.RecipeId > after && s.RecipeId <= upTo)
                    .Select(s => new { s.RecipeId, s.Fingerprint })
                    .ToDictionaryAsync(s => s.RecipeId, s => s.Fingerprint, cancellationToken);
                var ids = live.Where(id => id > after && id <= upTo).ToArray();
                var outcome = new (bool Changed, Reading? Reading)[ids.Length];
                Parallel.For(0, ids.Length, new ParallelOptions { CancellationToken = cancellationToken }, i =>
                {
                    var id = ids[i];
                    var recipe = byRecipe.TryGetValue(id, out var l) ? l : new List<LineRow>();
                    var row = recipeRows[id];
                    var book = DietRecipeText.FromBook(row.SourceUrl, row.ScrapedAtUtc) ? new BookText(row.Name, methods.GetValueOrDefault(id), row.SourceAttribution) : null;
                    var print = Print(recipe, servings[id], book, words.Signature);
                    var isChanged = !stored.TryGetValue(id, out var was) || was != print || (movedFoods.Count > 0 && recipe.Any(x => movedFoods.Contains(x.IngredientId)));
                    outcome[i] = (isChanged, isChanged || all ? Read(readers, id, recipe, print, book, words, foods) : null);
                });
                for (var i = 0; i < ids.Length; i++)
                {
                    if (outcome[i].Changed) changed.Add(ids[i]);
                    if (outcome[i].Reading is { } reading) readings[ids[i]] = reading;
                }
            }
            return (live, changed, readings);
        }

        private async Task RemoveRecipesAsync(ApplicationDbContext db, long buildId, List<long> recipeIds, CancellationToken cancellationToken)
        {
            if (recipeIds.Count == 0) return;
            var setIds = _sets.Keys.ToList();
            await InTransactionAsync(db, async () =>
            {
                foreach (var batch in recipeIds.Chunk(5000))
                {
                    await DeleteAsync(db, db.DietRecipeFlags.Where(f => setIds.Contains(f.SetId) && batch.Contains(f.RecipeId)), cancellationToken);
                    await DeleteAsync(db, db.DietRecipeStates.Where(s => s.BuildId == buildId && batch.Contains(s.RecipeId)), cancellationToken);
                }
            }, cancellationToken);
        }

        private async Task<(List<long> Decided, int Late)> DecideRecipesAsync(ApplicationDbContext db, long buildId, DietFoodCatalog catalog, List<SetState> sets, HashSet<long> live, HashSet<long> changed,
            Dictionary<long, Reading> readings, LogMark mark, CancellationToken cancellationToken)
        {
            var anyBuilding = sets.Any(s => s.Building);
            var recipes = (anyBuilding ? live : changed.Where(live.Contains)).OrderBy(id => id).ToList();
            var complete = sets.Where(s => !s.Building).Select(s => s.Row.Id).ToList();
            var capped = sets.Where(s => s.Capped != null).Select(s => s.Capped!.NutrientId!.Value).Distinct().ToList();
            var setIds = sets.Select(s => s.Row.Id).ToArray();
            var building = sets.Where(s => s.Building).ToDictionary(s => s.Row.Id);
            var decided = new List<long>();
            var lateCount = 0;
            foreach (var chunk in recipes.Chunk(RecipeChunk))
            {
                var retained = new Dictionary<long, Dictionary<long, RecipeAlcohol>>();
                foreach (var nutrientId in capped)
                {
                    var scope = chunk.Where(id => readings.TryGetValue(id, out var r) && (r.Drinks.Length > 0 || r.Foods.Any(f => catalog.Nutrients.TryGetValue(f, out var n) && n.TryGetValue(nutrientId, out var a) && a > DietCriteriaMatcher.PresencePer100g))).ToList();
                    retained[nutrientId] = scope.Count == 0 ? new Dictionary<long, RecipeAlcohol>() : await DietCriteriaMatcher.RecipeAlcoholAsync(db, nutrientId, scope);
                }
                var mine = chunk.Where(changed.Contains).ToList();
                var mineIds = mine.ToArray();
                var flags = new List<DietRecipeFlagEntity>();
                foreach (var set in sets)
                {
                    set.RecipeClock.Start();
                    foreach (var id in set.Building ? chunk : mineIds)
                    {
                        if (Decide(readings[id], id, set, retained) is not { } flag) continue;
                        flags.Add(new DietRecipeFlagEntity { SetId = set.Row.Id, RecipeId = id, Verdict = flag.Verdict, Reason = flag.Reason, IngredientId = flag.IngredientId, CriterionId = flag.CriterionId });
                    }
                    set.RecipeClock.Stop();
                }
                var states = mine.Select(id => new DietRecipeStateEntity { BuildId = buildId, RecipeId = id, Fingerprint = readings[id].Fingerprint, ComputedAt = DateTime.UtcNow, ReadXmin = mark.Xmin }).ToList();
                var counted = flags.Where(f => building.ContainsKey(f.SetId)).ToList();
                var late = new HashSet<long>();
                await InTransactionAsync(db, async () =>
                {
                    if (mine.Count > 0)
                    {
                        await DeleteAsync(db, db.DietRecipeFlags.Where(f => complete.Contains(f.SetId) && mine.Contains(f.RecipeId)), cancellationToken);
                        await DeleteAsync(db, db.DietRecipeStates.Where(s => s.BuildId == buildId && mine.Contains(s.RecipeId)), cancellationToken);
                    }
                    if (mark.Seen != null && mine.Count > 0)
                    {
                        late = await LateAsync(db, mark.Seen, mine, cancellationToken);
                        flags.RemoveAll(f => late.Contains(f.RecipeId));
                        states.RemoveAll(s => late.Contains(s.RecipeId));
                    }
                    await InsertFlagsAsync(db, flags, cancellationToken);
                    await InsertRecipeStatesAsync(db, states, cancellationToken);
                    if (mark.Snapshot != null && mine.Count > 0) late = await WithdrawLateAsync(db, buildId, setIds, mineIds, mark.Snapshot, cancellationToken);
                }, cancellationToken);
                foreach (var flag in counted.Where(f => !late.Contains(f.RecipeId)))
                {
                    if (flag.Verdict == DietVerdict.Contains) building[flag.SetId].Containing++;
                    else building[flag.SetId].Unknown++;
                }
                foreach (var id in mine.Where(id => !late.Contains(id))) _recipePrints[id] = readings[id].Fingerprint;
                decided.AddRange(mine.Where(id => !late.Contains(id)));
                lateCount += late.Count;
            }
            return (decided, lateCount);
        }

        internal Flag? Decide(Reading reading, long recipeId, SetState set, Dictionary<long, Dictionary<long, RecipeAlcohol>> retained)
        {
            foreach (var food in reading.Foods)
                if (set.Facts.TryGetValue(food, out var fact) && fact.Verdict == DietVerdict.Contains) return new Flag(DietVerdict.Contains, DietFlagReason.Food, food, fact.CriterionId);
            foreach (var key in reading.Rules)
                if (set.LineKeys.TryGetValue(key, out var criterion)) return new Flag(DietVerdict.Contains, DietFlagReason.Line, null, criterion);
            foreach (var food in reading.Foods)
            {
                if (!set.Facts.TryGetValue(food, out var fact) || fact.Verdict != DietVerdict.AliasOnly) continue;
                foreach (var key in reading.Rules)
                    if (set.AllKeys.TryGetValue(key, out var criterion)) return new Flag(DietVerdict.Contains, DietFlagReason.AliasLine, food, criterion);
                break;
            }
            if (set.DrinkCriterion is { } drinkCriterion)
                foreach (var drink in reading.Drinks)
                    if (!set.Facts.TryGetValue(drink.IngredientId, out var fact) || fact.Verdict != DietVerdict.Contains) return new Flag(DietVerdict.Contains, DietFlagReason.Drink, drink.IngredientId, drinkCriterion);
            if (set.Capped is { } capped && retained.TryGetValue(capped.NutrientId!.Value, out var counts) && counts.TryGetValue(recipeId, out var count) && count.Excludes(capped.MaxAmountPerServing!.Value))
                return new Flag(DietVerdict.Contains, DietFlagReason.Retained, null, capped.Id);
            LineWords? unknownLine = null;
            if (set.Spec.Severe && set.Vocabulary is { } vocabulary)
                foreach (var line in reading.Words)
                {
                    if (line.Choice) unknownLine ??= line;
                    if (!line.Read)
                    {
                        if (vocabulary.KindIn(line.Words) >= 0) return new Flag(DietVerdict.Contains, DietFlagReason.FoodWord, line.IngredientId, null);
                        if (line.Reference && line.Words.Length > 0 && !reading.Table!.Names(line.IngredientId, line.Words)) unknownLine ??= line;
                        continue;
                    }
                    if (line.Group >= 0 && line.Group < set.GroupClasses.Length)
                    {
                        var group = set.GroupClasses[line.Group];
                        if (group == DietFoodWords.WordClass.Flagged || vocabulary.KindIn(line.Words) >= 0) return new Flag(DietVerdict.Contains, DietFlagReason.FoodWord, line.IngredientId, null);
                        if (group == DietFoodWords.WordClass.Clear && vocabulary.Read(line.Words).Value != DietFoodWords.WordClass.Flagged) continue;
                    }
                    var value = vocabulary.Read(line.Words).Value;
                    if (value == DietFoodWords.WordClass.Flagged) return new Flag(DietVerdict.Contains, DietFlagReason.FoodWord, line.IngredientId, null);
                    if (value != DietFoodWords.WordClass.Clear || line.Reference) unknownLine ??= line;
                }
            if (reading.Lines == 0) return new Flag(DietVerdict.Unknown, DietFlagReason.NoLine, null, null);
            foreach (var food in reading.Foods)
                if (!_foodPrints.ContainsKey(food) || set.Facts.TryGetValue(food, out var fact) && fact.Verdict == DietVerdict.Unknown)
                    return new Flag(DietVerdict.Unknown, DietFlagReason.UnknownFood, food, null);
            if (!set.Spec.Severe) return null;
            if (unknownLine != null) return new Flag(DietVerdict.Unknown, DietFlagReason.UnknownWord, unknownLine.IngredientId, null);
            if (reading.ConsommeLine is { } consomme && reading.Book != null && set.ClarifiedBooks.Contains(reading.Book)) return new Flag(DietVerdict.Unknown, DietFlagReason.UnknownWord, consomme, null);
            foreach (var key in reading.TitleRules)
                if (set.LineKeys.TryGetValue(key, out var criterion)) return new Flag(DietVerdict.Unknown, DietFlagReason.Title, null, criterion);
            foreach (var key in reading.MethodRules)
                if (set.LineKeys.TryGetValue(key, out var criterion)) return new Flag(DietVerdict.Unknown, DietFlagReason.Method, null, criterion);
            if (reading.DoughLost) return new Flag(DietVerdict.Unknown, DietFlagReason.Title, null, null);
            if (reading.FlourLost) return new Flag(DietVerdict.Unknown, DietFlagReason.NoLine, null, null);
            if (set.Vocabulary is { } words)
            {
                foreach (var (word, next) in reading.MethodWords)
                    if (words.HoldsInMethod(word, next)) return new Flag(DietVerdict.Unknown, DietFlagReason.Method, null, null);
                if (reading.MethodFood != null) return new Flag(DietVerdict.Unknown, DietFlagReason.Method, null, null);
                if (reading.Lines == 1 && reading.MethodWords.Any(p => (words.ClassOf(p.Word) is DietFoodWords.WordClass.Flagged or DietFoodWords.WordClass.Both || words.FormOf(p.Word) == DietFoodWords.FormKind.Dish))) return new Flag(DietVerdict.Unknown, DietFlagReason.Method, null, null);
            }
            return null;
        }

        private static async Task RetireAsync(ApplicationDbContext db, long activeId, CancellationToken cancellationToken)
        {
            var old = await db.DietGateBuilds.Where(b => b.Id != activeId && b.State != DietGateStore.Retired && b.Id < activeId).ToListAsync(cancellationToken);
            foreach (var build in old) build.State = DietGateStore.Retired;
            await db.SaveChangesAsync(cancellationToken);
            var retired = await db.DietGateBuilds.AsNoTracking().Where(b => b.State == DietGateStore.Retired).Select(b => b.Id).ToListAsync(cancellationToken);
            foreach (var buildId in retired)
            {
                foreach (var setId in await db.DietGateSets.AsNoTracking().Where(s => s.BuildId == buildId).Select(s => s.Id).ToListAsync(cancellationToken))
                {
                    await DeleteSetRowsAsync(db, setId, cancellationToken);
                    await DeleteAsync(db, db.DietGateSets.Where(s => s.Id == setId), cancellationToken);
                }
                await DeleteAsync(db, db.DietFoodStates.Where(s => s.BuildId == buildId), cancellationToken);
                await DeleteAsync(db, db.DietRecipeStates.Where(s => s.BuildId == buildId), cancellationToken);
            }
        }

        private static async Task RemoveSupersededAsync(ApplicationDbContext db, long buildId, CancellationToken cancellationToken)
        {
            foreach (var setId in await db.DietGateSets.AsNoTracking().Where(s => s.BuildId == buildId && s.State == DietGateStore.Superseded).Select(s => s.Id).ToListAsync(cancellationToken))
            {
                await DeleteSetRowsAsync(db, setId, cancellationToken);
                await DeleteAsync(db, db.DietGateSets.Where(s => s.Id == setId), cancellationToken);
            }
        }

        private static async Task DeleteSetRowsAsync(ApplicationDbContext db, long setId, CancellationToken cancellationToken)
        {
            await DeleteAsync(db, db.DietFoodFacts.Where(f => f.SetId == setId), cancellationToken);
            await DeleteAsync(db, db.DietRecipeFlags.Where(f => f.SetId == setId), cancellationToken);
        }

        private static async Task InTransactionAsync(ApplicationDbContext db, Func<Task> work, CancellationToken cancellationToken)
        {
            if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null)
            {
                await work();
                return;
            }
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await work();
            await transaction.CommitAsync(cancellationToken);
        }

        private static async Task DeleteAsync<T>(ApplicationDbContext db, IQueryable<T> rows, CancellationToken cancellationToken) where T : class
        {
            if (db.Database.IsRelational())
            {
                await rows.ExecuteDeleteAsync(cancellationToken);
                return;
            }
            var found = await rows.ToListAsync(cancellationToken);
            if (found.Count == 0) return;
            db.RemoveRange(found);
            await db.SaveChangesAsync(cancellationToken);
        }

        private static async Task InsertAsync<T>(ApplicationDbContext db, IReadOnlyCollection<T> rows, string copy, Func<NpgsqlBinaryImporter, T, CancellationToken, Task> write, CancellationToken cancellationToken) where T : class
        {
            if (rows.Count == 0) return;
            if (db.Database.IsRelational() && db.Database.GetDbConnection() is NpgsqlConnection connection)
            {
                await db.Database.OpenConnectionAsync(cancellationToken);
                try
                {
                    await using var importer = await connection.BeginBinaryImportAsync(copy, cancellationToken);
                    foreach (var row in rows)
                    {
                        await importer.StartRowAsync(cancellationToken);
                        await write(importer, row, cancellationToken);
                    }
                    await importer.CompleteAsync(cancellationToken);
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
                return;
            }
            db.AddRange(rows);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var row in rows) db.Entry(row).State = EntityState.Detached;
        }

        private static async Task WriteAsync(NpgsqlBinaryImporter importer, long? value, CancellationToken cancellationToken)
        {
            if (value is { } v) await importer.WriteAsync(v, NpgsqlDbType.Bigint, cancellationToken);
            else await importer.WriteNullAsync(cancellationToken);
        }

        private static async Task WriteAsync(NpgsqlBinaryImporter importer, string? value, CancellationToken cancellationToken)
        {
            if (value != null) await importer.WriteAsync(value, NpgsqlDbType.Varchar, cancellationToken);
            else await importer.WriteNullAsync(cancellationToken);
        }

        private static Task InsertFactsAsync(ApplicationDbContext db, List<DietFoodFactEntity> rows, CancellationToken cancellationToken) =>
            InsertAsync(db, rows, "COPY plan.\"DietFoodFact\" (\"SetId\", \"IngredientId\", \"Verdict\", \"CriterionId\", \"CriterionIds\", \"Source\") FROM STDIN (FORMAT BINARY)", async (w, r, ct) =>
            {
                await w.WriteAsync(r.SetId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.IngredientId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync((short)r.Verdict, NpgsqlDbType.Smallint, ct);
                await WriteAsync(w, r.CriterionId, ct);
                await WriteAsync(w, r.CriterionIds, ct);
                await WriteAsync(w, r.Source, ct);
            }, cancellationToken);

        private static Task InsertFoodStatesAsync(ApplicationDbContext db, List<DietFoodStateEntity> rows, CancellationToken cancellationToken) =>
            InsertAsync(db, rows, "COPY plan.\"DietFoodState\" (\"BuildId\", \"IngredientId\", \"Fingerprint\", \"ComputedAt\") FROM STDIN (FORMAT BINARY)", async (w, r, ct) =>
            {
                await w.WriteAsync(r.BuildId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.IngredientId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.Fingerprint, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.ComputedAt, NpgsqlDbType.TimestampTz, ct);
            }, cancellationToken);

        private static Task InsertFlagsAsync(ApplicationDbContext db, List<DietRecipeFlagEntity> rows, CancellationToken cancellationToken) =>
            InsertAsync(db, rows, "COPY plan.\"DietRecipeFlag\" (\"SetId\", \"RecipeId\", \"Verdict\", \"Reason\", \"IngredientId\", \"CriterionId\") FROM STDIN (FORMAT BINARY)", async (w, r, ct) =>
            {
                await w.WriteAsync(r.SetId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.RecipeId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync((short)r.Verdict, NpgsqlDbType.Smallint, ct);
                await w.WriteAsync((short)r.Reason, NpgsqlDbType.Smallint, ct);
                await WriteAsync(w, r.IngredientId, ct);
                await WriteAsync(w, r.CriterionId, ct);
            }, cancellationToken);

        private static Task InsertRecipeStatesAsync(ApplicationDbContext db, List<DietRecipeStateEntity> rows, CancellationToken cancellationToken) =>
            InsertAsync(db, rows, "COPY plan.\"DietRecipeState\" (\"BuildId\", \"RecipeId\", \"Fingerprint\", \"ComputedAt\", \"ReadXmin\") FROM STDIN (FORMAT BINARY)", async (w, r, ct) =>
            {
                await w.WriteAsync(r.BuildId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.RecipeId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.Fingerprint, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.ComputedAt, NpgsqlDbType.TimestampTz, ct);
                if (r.ReadXmin is { } xmin) await w.WriteAsync(xmin, NpgsqlDbType.Xid8, ct);
                else await w.WriteNullAsync(ct);
            }, cancellationToken);
    }
}
