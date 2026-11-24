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
using Npgsql;
using NpgsqlTypes;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The diet gate's build job (staging checkpoint 5, Q2, Q3): brings the stored results of the current rule version
    /// (<see cref="DietGateStore.Version"/>) up to date in one cycle. It reads the food catalog once
    /// (<see cref="DietFoodCatalog"/>) and decides, with the matcher's own reading (<see cref="DietFoodReader"/>), every
    /// food whose fingerprint changed (every food for a set not yet built); it reads every live recipe line in windows of
    /// recipe ids and reads each changed recipe's lines with the line index's matcher (<see cref="DietLineIndex.Rules"/>),
    /// then decides, per restriction set, every recipe whose lines or servings changed or one of whose foods changed
    /// (every recipe for a set not yet built), with the retained alcohol counted for those recipes only
    /// (<see cref="DietCriteriaMatcher.RecipeAlcoholAsync"/>). A recipe's flags and state are replaced together, a chunk of
    /// recipes in one transaction. A new rule version is built as a new build beside the active one, which requests keep
    /// reading until the new build is complete; the old build is then retired and its rows removed. The job holds what
    /// it has decided in memory between cycles and reloads it from the stored rows after a restart.
    /// </summary>
    public sealed class DietGateStoreBuilder
    {
        /// <summary>How many recipe ids one read of the lines covers.</summary>
        public const int LineWindow = 2000;

        /// <summary>How many recipes are decided and written in one transaction.</summary>
        public const int RecipeChunk = 5000;

        /// <summary>What one cycle did: the build, the sets (and those built in this cycle, with their timings), the foods and recipes decided again, and the time each part took.</summary>
        public sealed record CycleReport(long BuildId, string Version, string State, bool Switched, int Sets, int FoodsDecided, int Foods, int RecipesDecided, int Recipes,
            double FoodSeconds, double RecipeSeconds, double Seconds, IReadOnlyList<DietGateSetReport> Built);

        private sealed record Spec(long TypeId, int MinimumSeverity, string Key, string Text, bool Severe, List<DietDataCriterion> Criteria);

        private sealed record Fact(DietVerdict Verdict, long? CriterionId, string? CriterionIds, string? Source);

        private sealed record Reading(long Fingerprint, string[] Rules, DietCriteriaMatcher.RecipeLine[] Drinks, long[] Foods, int Lines);

        private sealed record Flag(DietVerdict Verdict, DietFlagReason Reason, long? IngredientId, long? CriterionId);

        private sealed class SetState
        {
            public SetState(DietGateSetEntity row) => Row = row;

            public DietGateSetEntity Row { get; }

            public Spec Spec { get; set; } = default!;

            public Dictionary<long, Fact> Facts { get; } = new();

            public Dictionary<string, long> LineKeys { get; set; } = new(StringComparer.Ordinal);

            public Dictionary<string, long> AllKeys { get; set; } = new(StringComparer.Ordinal);

            public long? DrinkCriterion { get; set; }

            public DietDataCriterion? Capped { get; set; }

            public bool Building => Row.State == DietGateStore.Building;

            public Stopwatch FoodClock { get; } = new();

            public Stopwatch RecipeClock { get; } = new();

            public int Containing { get; set; }

            public int Unknown { get; set; }
        }

        private readonly Func<string> _version;
        private long _buildId = -1;
        private readonly Dictionary<long, long> _foodPrints = new();
        private readonly Dictionary<long, long> _recipePrints = new();
        private readonly Dictionary<long, Reading> _readings = new();
        private readonly Dictionary<long, SetState> _sets = new();
        private string _rulesSignature = string.Empty;

        /// <summary>Run after the foods of a cycle are decided and stored and before its recipes are (a test makes a cycle fail there).</summary>
        internal Func<CancellationToken, Task>? BeforeRecipes { get; set; }

        /// <summary>A build job for the current rule version, or for the version <paramref name="version"/> gives.</summary>
        public DietGateStoreBuilder(Func<string>? version = null) => _version = version ?? (() => DietGateStore.Version);

        /// <summary>Runs one cycle on the context's database.</summary>
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

        private async Task<CycleReport> CycleAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
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
            var nutrientIds = specs.SelectMany(s => s.Criteria).Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).Distinct().ToList();
            var catalog = await DietFoodCatalog.LoadAsync(db, nutrientIds, cancellationToken);
            var prints = catalog.Foods.Keys.ToDictionary(id => id, catalog.Fingerprint);
            var changedFoods = prints.Where(p => !_foodPrints.TryGetValue(p.Key, out var old) || old != p.Value).Select(p => p.Key).ToHashSet();
            var goneFoods = _foodPrints.Keys.Where(id => !prints.ContainsKey(id)).ToList();
            await DecideFoodsAsync(db, catalog, sets, changedFoods, goneFoods, cancellationToken);
            foreach (var id in goneFoods) _foodPrints.Remove(id);
            foreach (var id in changedFoods) _foodPrints[id] = prints[id];
            foodClock.Stop();

            var recipeClock = Stopwatch.StartNew();
            if (BeforeRecipes != null) await BeforeRecipes(cancellationToken);
            var (live, changedRecipes) = await ReadLinesAsync(db, build.Id, specs, cancellationToken);
            foreach (var (id, reading) in _readings)
                if (!changedRecipes.Contains(id) && (reading.Foods.Any(changedFoods.Contains) || reading.Foods.Any(goneFoods.Contains))) changedRecipes.Add(id);
            var goneRecipes = _recipePrints.Keys.Where(id => !live.Contains(id)).ToList();
            await RemoveRecipesAsync(db, build.Id, goneRecipes, cancellationToken);
            foreach (var id in goneRecipes) _recipePrints.Remove(id);
            var decided = await DecideRecipesAsync(db, build.Id, catalog, sets, live, changedRecipes, cancellationToken);
            await WriteFoodStatesAsync(db, build.Id, catalog, changedFoods, goneFoods, cancellationToken);
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
            build.LastCycleRecipes = decided;
            build.Note = null;
            await db.SaveChangesAsync(cancellationToken);
            await RemoveSupersededAsync(db, build.Id, cancellationToken);
            if (switched) await RetireAsync(db, build.Id, cancellationToken);

            return new CycleReport(build.Id, build.Version, build.State, switched, sets.Count, changedFoods.Count, catalog.Foods.Count, decided, live.Count,
                foodClock.Elapsed.TotalSeconds, recipeClock.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds, reports);
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
            _recipePrints.Clear();
            _readings.Clear();
            _sets.Clear();
            _rulesSignature = string.Empty;
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

        private static async Task<List<Spec>> SpecsAsync(ApplicationDbContext db)
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
            foreach (var set in _sets.Values)
            {
                var spec = wanted[set.Row.CriteriaKey];
                set.Spec = spec;
                set.LineKeys = DietCriteriaMatcher.LinePatterns(spec.Criteria)
                    .Select(c => (Key: DietLineIndex.KeyOf(c), c))
                    .Where(x => x.Key != null)
                    .GroupBy(x => x.Key!, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => Top(g.Select(x => x.c)).Id, StringComparer.Ordinal);
                set.AllKeys = spec.Criteria
                    .Select(c => (Key: DietLineIndex.KeyOf(c), c))
                    .Where(x => x.Key != null)
                    .GroupBy(x => x.Key!, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => Top(g.Select(x => x.c)).Id, StringComparer.Ordinal);
                set.DrinkCriterion = DietCriteriaMatcher.ReadsLines(spec.Criteria)
                    ? Top(spec.Criteria.Where(c => c.NutrientId != null && c.MaxAmountPerServing == null && DietCriteriaMatcher.IsAlcohol(c.NutrientName))).Id
                    : null;
                set.Capped = DietCriteriaMatcher.RetainedCriterion(spec.Criteria);
            }
        }

        private static DietDataCriterion Top(IEnumerable<DietDataCriterion> criteria) => criteria.OrderByDescending(c => c.Severity).ThenBy(c => c.Id).First();

        private async Task DecideFoodsAsync(ApplicationDbContext db, DietFoodCatalog catalog, List<SetState> sets, HashSet<long> changedFoods, List<long> goneFoods, CancellationToken cancellationToken)
        {
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
            return result;
        }

        private async Task<(HashSet<long> Live, HashSet<long> Changed)> ReadLinesAsync(ApplicationDbContext db, long buildId, List<Spec> specs, CancellationToken cancellationToken)
        {
            var rules = DietLineIndex.Rules(specs.SelectMany(s => s.Criteria));
            var signature = string.Join('\u0001', rules.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));
            var forceRules = signature != _rulesSignature;
            _rulesSignature = signature;
            var matcher = new DietLineIndex.Matcher(rules);
            var servings = (await db.Recipes.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => !r.IsDeleted)
                    .Select(r => new { r.Id, r.Servings, r.RecipeServings })
                    .ToListAsync(cancellationToken))
                .ToDictionary(r => r.Id, r => r.Servings?.ToString(CultureInfo.InvariantCulture) + "/" + r.RecipeServings?.ToString(CultureInfo.InvariantCulture));
            var live = servings.Keys.ToHashSet();
            var changed = new HashSet<long>();
            var max = live.Count == 0 ? 0 : live.Max();
            for (var after = 0L; after < max; after += LineWindow)
            {
                var upTo = Math.Min(after + LineWindow, max);
                var lines = await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(ri => !ri.IsDeleted && ri.RecipeId > after && ri.RecipeId <= upTo)
                    .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis })
                    .ToListAsync(cancellationToken);
                var byRecipe = lines.GroupBy(l => l.RecipeId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.IngredientId).ToList());
                var stored = await db.DietRecipeStates.AsNoTracking()
                    .Where(s => s.BuildId == buildId && s.RecipeId > after && s.RecipeId <= upTo)
                    .Select(s => new { s.RecipeId, s.Fingerprint })
                    .ToDictionaryAsync(s => s.RecipeId, s => s.Fingerprint, cancellationToken);
                var ids = live.Where(id => id > after && id <= upTo).ToArray();
                var readings = new Reading?[ids.Length];
                Parallel.For(0, ids.Length, new ParallelOptions { CancellationToken = cancellationToken }, i =>
                {
                    var id = ids[i];
                    var recipe = byRecipe.TryGetValue(id, out var l) ? l : new();
                    var hash = new DietFoodCatalog.Fnv();
                    foreach (var line in recipe)
                        hash.Add(line.IngredientId).Add(line.SortOrder).Add(line.RawLine).Add(line.Quantity).Add(line.MeasurementId).Add(line.LineKind).Add(line.AlcoholRetention).Add(line.AlcoholRetentionBasis);
                    hash.Add("|").Add(servings[id]);
                    var print = hash.Value;
                    if (!forceRules && _readings.TryGetValue(id, out var kept) && kept.Fingerprint == print) return;
                    var (matched, drinks) = matcher.Read(recipe.Select(x => new DietLineIndex.Line(x.RecipeId, x.IngredientId, x.SortOrder, x.RawLine)).ToList(), true);
                    readings[i] = new Reading(print, matched, drinks, recipe.Select(x => x.IngredientId).Distinct().ToArray(), recipe.Count);
                });
                for (var i = 0; i < ids.Length; i++)
                {
                    if (readings[i] is { } reading) _readings[ids[i]] = reading;
                    if (!stored.TryGetValue(ids[i], out var print) || print != _readings[ids[i]].Fingerprint) changed.Add(ids[i]);
                }
            }
            foreach (var gone in _readings.Keys.Where(id => !live.Contains(id)).ToList()) _readings.Remove(gone);
            return (live, changed);
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

        private async Task<int> DecideRecipesAsync(ApplicationDbContext db, long buildId, DietFoodCatalog catalog, List<SetState> sets, HashSet<long> live, HashSet<long> changed, CancellationToken cancellationToken)
        {
            var anyBuilding = sets.Any(s => s.Building);
            var recipes = (anyBuilding ? live : changed.Where(live.Contains)).OrderBy(id => id).ToList();
            var complete = sets.Where(s => !s.Building).Select(s => s.Row.Id).ToList();
            var capped = sets.Where(s => s.Capped != null).Select(s => s.Capped!.NutrientId!.Value).Distinct().ToList();
            var decided = 0;
            foreach (var chunk in recipes.Chunk(RecipeChunk))
            {
                var retained = new Dictionary<long, Dictionary<long, RecipeAlcohol>>();
                foreach (var nutrientId in capped)
                {
                    var scope = chunk.Where(id => _readings.TryGetValue(id, out var r) && (r.Drinks.Length > 0 || r.Foods.Any(f => catalog.Nutrients.TryGetValue(f, out var n) && n.TryGetValue(nutrientId, out var a) && a > DietCriteriaMatcher.PresencePer100g))).ToList();
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
                        if (Decide(id, set, retained) is not { } flag) continue;
                        flags.Add(new DietRecipeFlagEntity { SetId = set.Row.Id, RecipeId = id, Verdict = flag.Verdict, Reason = flag.Reason, IngredientId = flag.IngredientId, CriterionId = flag.CriterionId });
                        if (!set.Building) continue;
                        if (flag.Verdict == DietVerdict.Contains) set.Containing++;
                        else set.Unknown++;
                    }
                    set.RecipeClock.Stop();
                }
                var states = mine.Select(id => new DietRecipeStateEntity { BuildId = buildId, RecipeId = id, Fingerprint = _readings[id].Fingerprint, ComputedAt = DateTime.UtcNow }).ToList();
                await InTransactionAsync(db, async () =>
                {
                    if (mine.Count > 0)
                    {
                        await DeleteAsync(db, db.DietRecipeFlags.Where(f => complete.Contains(f.SetId) && mine.Contains(f.RecipeId)), cancellationToken);
                        await DeleteAsync(db, db.DietRecipeStates.Where(s => s.BuildId == buildId && mine.Contains(s.RecipeId)), cancellationToken);
                    }
                    await InsertFlagsAsync(db, flags, cancellationToken);
                    await InsertRecipeStatesAsync(db, states, cancellationToken);
                }, cancellationToken);
                foreach (var id in mine) _recipePrints[id] = _readings[id].Fingerprint;
                decided += mine.Count;
            }
            return decided;
        }

        private Flag? Decide(long recipeId, SetState set, Dictionary<long, Dictionary<long, RecipeAlcohol>> retained)
        {
            var reading = _readings[recipeId];
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
            if (reading.Lines == 0) return new Flag(DietVerdict.Unknown, DietFlagReason.NoLine, null, null);
            foreach (var food in reading.Foods)
                if (!_foodPrints.ContainsKey(food)) return new Flag(DietVerdict.Unknown, DietFlagReason.UnknownFood, food, null);
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
            InsertAsync(db, rows, "COPY plan.\"DietRecipeState\" (\"BuildId\", \"RecipeId\", \"Fingerprint\", \"ComputedAt\") FROM STDIN (FORMAT BINARY)", async (w, r, ct) =>
            {
                await w.WriteAsync(r.BuildId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.RecipeId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.Fingerprint, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(r.ComputedAt, NpgsqlDbType.TimestampTz, ct);
            }, cancellationToken);
    }
}
