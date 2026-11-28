using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Orch.Services.Support
{
    /// <summary>The stored restriction sets a household or person set reads (staging checkpoint 5, Q2): the active build, every set's id, and the sets whose unknown recipes and foods are held (a severe set, or a type a member holds at severity 4 or 5).</summary>
    public sealed class StoredDietGate
    {
        public StoredDietGate(long buildId, IReadOnlyList<long> setIds, IReadOnlyList<long> unknownSetIds, IReadOnlyDictionary<long, long> setByType)
        {
            BuildId = buildId;
            SetIds = setIds;
            UnknownSetIds = unknownSetIds;
            SetByType = setByType;
        }

        public long BuildId { get; }

        public IReadOnlyList<long> SetIds { get; }

        /// <summary>The sets for which a recipe or food that is not decided, or decided as unknown, is held.</summary>
        public IReadOnlyList<long> UnknownSetIds { get; }

        /// <summary>The set read for each restriction type.</summary>
        public IReadOnlyDictionary<long, long> SetByType { get; }
    }

    /// <summary>
    /// The diet gate's stored results (staging checkpoint 5, Q2, Q3, Q4, Q6, P23). The warm in the API's memory failed
    /// twice (its probes passed the command timeout under load, and a failed refresh dropped the value), so the gate's
    /// results live in the database: a fact per food and restriction set (<see cref="DietFoodFactEntity"/>) and a flag per
    /// recipe and set (<see cref="DietRecipeFlagEntity"/>), written only by the build job
    /// (<see cref="DietGateStoreBuilder"/>). Every request reads these indexed rows and nothing else: it never runs a
    /// probe, never waits on a build and never loses a value because a build failed. A set is found by the key of its
    /// criteria (<see cref="KeyOf"/>) in the active build (<see cref="Version"/>); while the set of changed criteria is
    /// being built the type's previous set is read, and a type the build has never decided holds every recipe. A recipe
    /// the build has not decided (new, or its lines written since) is held by every set. A food the build has not
    /// decided, a recipe with no live ingredient line and a line on a food not decided count as unknown, held for a
    /// severe set.
    /// </summary>
    public static class DietGateStore
    {
        /// <summary>The store's own version; raise it when what the build writes changes for the same rules.</summary>
        public const string StoreVersion = "diet-store/1";

        public const string Building = "building";
        public const string Active = "active";
        public const string Retired = "retired";
        public const string Complete = "complete";
        public const string Superseded = "superseded";

        /// <summary>The rule version a build is made under: a change builds a new build beside the active one.</summary>
        public static string Version => string.Join(';', StoreVersion, DietPatternRule.Version, StatementLabelTerms.Version, DietDishes.Version, DietVocabulary.Version, DietFoodWords.Version, DietRecipeText.Version, DietMeasureWords.Version, AlcoholLineContext.Version, IngredientStatementReader.Version, BrandedConsensus.Signature, DietDishWords.Signature, IngredientLineParser.ReferralSignature, DietSecondGate.StoreEntry);

        /// <summary>The criteria of a set as the gate reads them, without the rule version.</summary>
        public static string CriteriaText(IEnumerable<DietDataCriterion> criteria) =>
            string.Join(';', criteria.OrderBy(c => c.Id).Select(c => string.Create(CultureInfo.InvariantCulture,
                $"{c.Id}:{c.Severity}:{c.WordPattern}:{c.LikePattern}:{c.NutrientId}:{c.NutrientName}:{c.MaxAmountPerServing}:{c.Rule?.TypeName}")));

        /// <summary>The key a set is found by: the SHA-256 of <see cref="CriteriaText"/>, lower-case hex.</summary>
        public static string KeyOf(IEnumerable<DietDataCriterion> criteria) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CriteriaText(criteria)))).ToLowerInvariant();

        /// <summary>Whether requests read the store: always on a relational database; on another, once a build is active (tests that build one).</summary>
        public static async Task<bool> InUseAsync(ApplicationDbContext db) =>
            db.Database.IsRelational() || await db.DietGateBuilds.AsNoTracking().AnyAsync(b => b.State == Active);

        /// <summary>
        /// The recipe states that stand: a state whose reading of the lines could not see a write logged since for its recipe
        /// (a row of <c>plan."DietGateWrite"</c> whose transaction is not older than <see cref="DietRecipeStateEntity.ReadXmin"/>)
        /// does not, so the recipe is held as undecided from the moment that write commits, although the writer's trigger
        /// found no state to remove while the build job's chunk was not yet committed, until a cycle decides it again (review ck12ls-r1).
        /// On Postgres a state stored without a <see cref="DietRecipeStateEntity.ReadXmin"/> (before the column existed) does
        /// not stand while any write is logged for its recipe, since a food write no longer removes it (review ck16-r5).
        /// </summary>
        public static IQueryable<DietRecipeStateEntity> Standing(this IQueryable<DietRecipeStateEntity> states, ApplicationDbContext db) =>
            db.Database.IsRelational()
                ? states.Where(s => !db.DietGateWrites.Any(w => w.RecipeId == s.RecipeId && (s.ReadXmin == null || w.Tx >= s.ReadXmin)))
                : states.Where(s => !db.DietGateWrites.Any(w => w.RecipeId == s.RecipeId && w.Tx >= s.ReadXmin));

        /// <summary>The build requests read, or null before the first build is complete.</summary>
        public static Task<long?> ActiveBuildAsync(ApplicationDbContext db) =>
            db.DietGateBuilds.AsNoTracking().Where(b => b.State == Active).OrderByDescending(b => b.Id).Select(b => (long?)b.Id).FirstOrDefaultAsync();

        /// <summary>A set of the active build a request can read for a list of criteria.</summary>
        internal sealed record SetRow(long Id, long TypeId, string CriteriaKey, int MinimumSeverity, bool Severe, string State);

        /// <summary>
        /// The set each list of criteria reads in the build: the complete set of its key, or, while that set is not built
        /// (criteria changed by a deploy or an admin, review CK10S-2), the type's previous set of the same reading, which
        /// the build keeps until the new one is complete (a list of severe criteria only may read the type's whole set,
        /// which holds more); null for a list of a type the build has never decided, and null for a severe type whose
        /// criteria changed (an admin's edit, a renamed type or nutrient): its recipes are held until the new set is built,
        /// since the previous set never read what the new criteria restrict (lead answer to CK19SM-3, wave 31).
        /// </summary>
        internal static async Task<List<(IReadOnlyList<DietDataCriterion> List, SetRow? Set)>> MatchAsync(ApplicationDbContext db, long build, IReadOnlyList<IReadOnlyList<DietDataCriterion>> lists)
        {
            var keys = lists.Select(KeyOf).ToList();
            var types = lists.Select(l => l[0].RestrictionTypeId).Distinct().ToList();
            var rows = await db.DietGateSets.AsNoTracking()
                .Where(s => s.BuildId == build && (s.State == Complete || s.State == Superseded) && (keys.Contains(s.CriteriaKey) || types.Contains(s.RestrictionTypeId)))
                .Select(s => new SetRow(s.Id, s.RestrictionTypeId, s.CriteriaKey, s.MinimumSeverity, s.Severe, s.State))
                .ToListAsync();
            return lists.Zip(keys, (list, key) => (list, Pick(rows, list, key))).ToList();
        }

        private static SetRow? Pick(IReadOnlyList<SetRow> rows, IReadOnlyList<DietDataCriterion> list, string key)
        {
            var exact = rows.Where(r => r.State == Complete && string.Equals(r.CriteriaKey, key, StringComparison.Ordinal)).OrderByDescending(r => r.Id).FirstOrDefault();
            if (exact != null) return exact;
            if (list.Any(c => c.Severity >= 4) || rows.Any(r => r.TypeId == list[0].RestrictionTypeId && r.Severe)) return null;
            var severeOnly = list.All(c => c.Severity >= 4);
            return rows
                .Where(r => r.TypeId == list[0].RestrictionTypeId && !string.Equals(r.CriteriaKey, key, StringComparison.Ordinal) && (r.MinimumSeverity == 0 || severeOnly))
                .OrderBy(r => severeOnly && r.MinimumSeverity >= 4 ? 0 : 1)
                .ThenBy(r => r.State == Superseded ? 0 : 1)
                .ThenByDescending(r => r.Id)
                .FirstOrDefault();
        }

        /// <summary>The sets of the active build for each type's criteria (<see cref="MatchAsync"/>), or null when there is no active build or a type has no set the build has decided (the request then holds every recipe).</summary>
        public static async Task<StoredDietGate?> SetsAsync(ApplicationDbContext db, IEnumerable<IReadOnlyList<DietDataCriterion>> lists, IReadOnlySet<long> severeTypes)
        {
            if (await ActiveBuildAsync(db) is not { } build) return null;
            var wanted = lists.Where(l => l.Count > 0).ToList();
            if (wanted.Count == 0) return new StoredDietGate(build, Array.Empty<long>(), Array.Empty<long>(), new Dictionary<long, long>());
            var matched = await MatchAsync(db, build, wanted);
            if (matched.Any(m => m.Set == null)) return null;
            var byType = new Dictionary<long, long>();
            foreach (var (list, set) in matched) byType[list[0].RestrictionTypeId] = set!.Id;
            var unknown = matched.Where(m => m.Set!.Severe || m.List.Any(c => c.Severity >= 4) || severeTypes.Contains(m.List[0].RestrictionTypeId)).Select(m => m.Set!.Id).Distinct().ToList();
            return new StoredDietGate(build, matched.Select(m => m.Set!.Id).Distinct().ToList(), unknown, byType);
        }

        /// <summary>
        /// The recipes the set lets through: none while it is pending; otherwise none with a live line on a food the
        /// restrictions name directly or count, none it flags by recipe, and, from the store, none the active build has not
        /// decided (a new recipe, or one whose lines were written since, review CK10S-3), none a stored set flags as
        /// containing what it restricts, and for a set that holds unknowns none flagged unknown.
        /// </summary>
        public static IQueryable<RecipeEntity> WithoutHeld(this IQueryable<RecipeEntity> recipes, ApplicationDbContext db, RestrictedIngredientSet set)
        {
            if (set.Pending) return recipes.Where(r => false);
            if (set.IngredientIds.Count > 0)
            {
                var foods = set.IngredientIds.ToList();
                recipes = recipes.Where(r => !r.RecipeIngredients!.Any(ri => foods.Contains(ri.IngredientId)));
            }
            if (set.RecipeIds.Count > 0)
            {
                var held = set.RecipeIds.ToList();
                recipes = recipes.Where(r => !held.Contains(r.Id));
            }
            if (set.Stored is { SetIds.Count: > 0 } stored)
            {
                var sets = stored.SetIds.ToList();
                var unknown = stored.UnknownSetIds.ToList();
                var build = stored.BuildId;
                recipes = recipes.Where(r => !db.DietRecipeFlags.Any(f => f.RecipeId == r.Id && sets.Contains(f.SetId)
                    && (f.Verdict == DietVerdict.Contains || unknown.Contains(f.SetId))));
                recipes = recipes.Where(r => db.DietRecipeStates.Standing(db).Any(s => s.BuildId == build && s.RecipeId == r.Id));
            }
            return recipes;
        }

        /// <summary>The foods the set lets through: none while it is pending; otherwise none it names or counts, and, from the store, none a stored set counts, and none the active build has not decided (new, or made undecided by a write since: staging checkpoint 6, R12).</summary>
        public static IQueryable<IngredientEntity> WithoutHeld(this IQueryable<IngredientEntity> foods, ApplicationDbContext db, RestrictedIngredientSet set)
        {
            if (set.Pending) return foods.Where(i => false);
            if (set.IngredientIds.Count > 0)
            {
                var ids = set.IngredientIds.ToList();
                foods = foods.Where(i => !ids.Contains(i.Id));
            }
            if (set.Stored is { SetIds.Count: > 0 } stored)
            {
                var sets = stored.SetIds.ToList();
                var unknown = stored.UnknownSetIds.ToList();
                var build = stored.BuildId;
                foods = foods.Where(i => !db.DietFoodFacts.Any(f => f.IngredientId == i.Id && sets.Contains(f.SetId)
                    && (f.Verdict == DietVerdict.Contains || (f.Verdict == DietVerdict.Unknown && unknown.Contains(f.SetId)))));
                foods = foods.Where(i => db.DietFoodStates.Any(s => s.BuildId == build && s.IngredientId == i.Id));
            }
            return foods;
        }

        /// <summary>Of the foods, those a stored set of the set counts (none outside the store).</summary>
        public static async Task<HashSet<long>> CountedAsync(ApplicationDbContext db, RestrictedIngredientSet set, IReadOnlyCollection<long> foodIds)
        {
            if (set.Stored is not { SetIds.Count: > 0 } stored || foodIds.Count == 0) return new HashSet<long>();
            var sets = stored.SetIds.ToList();
            var result = new HashSet<long>();
            foreach (var batch in foodIds.Distinct().Chunk(5000))
                result.UnionWith(await db.DietFoodFacts.AsNoTracking()
                    .Where(f => sets.Contains(f.SetId) && batch.Contains(f.IngredientId) && f.Verdict == DietVerdict.Contains)
                    .Select(f => f.IngredientId)
                    .ToListAsync());
            return result;
        }

        /// <summary>Of the foods, those the active build has not decided (new, or made undecided by a write since), for any stored set (none otherwise).</summary>
        public static async Task<HashSet<long>> UndecidedAsync(ApplicationDbContext db, RestrictedIngredientSet set, IReadOnlyCollection<long> foodIds)
        {
            if (set.Stored is not { SetIds.Count: > 0 } stored || foodIds.Count == 0) return new HashSet<long>();
            var build = stored.BuildId;
            var ids = foodIds.Distinct().ToList();
            var decided = new HashSet<long>();
            foreach (var batch in ids.Chunk(5000))
                decided.UnionWith(await db.DietFoodStates.AsNoTracking().Where(s => s.BuildId == build && batch.Contains(s.IngredientId)).Select(s => s.IngredientId).ToListAsync());
            return ids.Where(id => !decided.Contains(id)).ToHashSet();
        }

        /// <summary>What the recipe page reads for a person's restriction types from the store: each type's foods by criterion and alias-only foods among the given foods, the types whose set is not complete, the recipes a severe set could not decide or holds by a line's food word, and of those the ones its title, method or a line's word holds (by type and recipe, the reason and the criterion that named the food).</summary>
        public sealed record PageReading(Dictionary<long, DietCriteriaMatcher.TypeProbe> Probes, HashSet<long> PendingTypes, Dictionary<long, HashSet<long>> UnknownByType)
        {
            public Dictionary<(long Type, long Recipe), (DietFlagReason Reason, long? CriterionId)> TextHeld { get; } = new();

            /// <summary>The food word of each judged hold among the recipes, by type and recipe (<see cref="DietFlagReason.Judged"/>).</summary>
            public Dictionary<(long Type, long Recipe), string> JudgedFoods { get; } = new();
        }

        /// <summary>The recipe page's reading from the store, or null when requests do not read the store.</summary>
        public static async Task<PageReading?> PageAsync(ApplicationDbContext db, IReadOnlyList<DietDataCriterion> criteria, IReadOnlyCollection<long> foodIds, IReadOnlyCollection<long> recipeIds)
        {
            if (!await InUseAsync(db)) return null;
            var probes = new Dictionary<long, DietCriteriaMatcher.TypeProbe>();
            var pending = new HashSet<long>();
            var unknown = new Dictionary<long, HashSet<long>>();
            var build = await ActiveBuildAsync(db);
            var lists = criteria.GroupBy(c => c.RestrictionTypeId).Select(g => g.ToList()).ToList();
            if (build == null)
            {
                pending.UnionWith(lists.Select(l => l[0].RestrictionTypeId));
                return new PageReading(probes, pending, unknown);
            }
            var matched = await MatchAsync(db, build.Value, lists);
            var setIds = matched.Where(m => m.Set != null).Select(m => m.Set!.Id).Distinct().ToList();
            var foods = foodIds.Distinct().ToList();
            var facts = setIds.Count == 0 || foods.Count == 0 ? new List<DietFoodFactEntity>() : await db.DietFoodFacts.AsNoTracking()
                .Where(f => setIds.Contains(f.SetId) && foods.Contains(f.IngredientId))
                .ToListAsync();
            var recipes = recipeIds.Distinct().ToList();
            var flags = setIds.Count == 0 || recipes.Count == 0 ? new List<DietRecipeFlagEntity>() : await db.DietRecipeFlags.AsNoTracking()
                .Where(f => setIds.Contains(f.SetId) && recipes.Contains(f.RecipeId) && (f.Verdict == DietVerdict.Unknown || f.Reason == DietFlagReason.FoodWord || f.Reason == DietFlagReason.Judged))
                .ToListAsync();
            var judgedFoods = !flags.Any(f => f.Reason == DietFlagReason.Judged) ? new Dictionary<(long, long), string>() : (await db.RecipeDietJudgedHolds.AsNoTracking()
                    .Where(h => recipes.Contains(h.RecipeId))
                    .Select(h => new { h.RestrictionTypeId, h.RecipeId, h.Food })
                    .ToListAsync())
                .ToDictionary(h => (h.RestrictionTypeId, h.RecipeId), h => h.Food);
            var decided = recipes.Count == 0 ? new HashSet<long>() : (await db.DietRecipeStates.AsNoTracking().Standing(db)
                    .Where(s => s.BuildId == build && recipes.Contains(s.RecipeId))
                    .Select(s => s.RecipeId)
                    .ToListAsync())
                .ToHashSet();
            var textHeld = new Dictionary<(long, long), (DietFlagReason, long?)>();
            foreach (var (list, set) in matched)
            {
                var type = list[0].RestrictionTypeId;
                if (set == null)
                {
                    pending.Add(type);
                    continue;
                }
                var byCriterion = new Dictionary<long, HashSet<long>>();
                var aliasOnly = new HashSet<long>();
                foreach (var fact in facts.Where(f => f.SetId == set.Id))
                {
                    if (fact.Verdict == DietVerdict.AliasOnly)
                    {
                        aliasOnly.Add(fact.IngredientId);
                        continue;
                    }
                    if (fact.Verdict != DietVerdict.Contains) continue;
                    foreach (var id in ParseIds(fact.CriterionIds ?? fact.CriterionId?.ToString(CultureInfo.InvariantCulture)))
                    {
                        if (!byCriterion.TryGetValue(id, out var ids)) byCriterion[id] = ids = new HashSet<long>();
                        ids.Add(fact.IngredientId);
                    }
                }
                probes[type] = new DietCriteriaMatcher.TypeProbe(byCriterion, aliasOnly, new HashSet<long>(), new HashSet<long>());
                if (set.Severe || list.Any(c => c.Severity >= 4))
                {
                    var held = flags.Where(f => f.SetId == set.Id).Select(f => f.RecipeId).ToHashSet();
                    foreach (var f in flags.Where(f => f.SetId == set.Id && f.Reason is DietFlagReason.Title or DietFlagReason.Method or DietFlagReason.UnknownWord or DietFlagReason.FoodWord or DietFlagReason.Referral or DietFlagReason.SecondUnread or DietFlagReason.SecondReader or DietFlagReason.Judged))
                        textHeld[(type, f.RecipeId)] = (f.Reason, f.CriterionId);
                    held.UnionWith(recipes.Where(id => !decided.Contains(id)));
                    if (held.Count > 0) unknown[type] = held;
                }
                else if (flags.Where(f => f.SetId == set.Id && f.Reason == DietFlagReason.Judged).Select(f => f.RecipeId).ToHashSet() is { Count: > 0 } judgedHeld)
                {
                    foreach (var id in judgedHeld) textHeld[(type, id)] = (DietFlagReason.Judged, null);
                    unknown[type] = judgedHeld;
                }
            }
            var reading = new PageReading(probes, pending, unknown);
            foreach (var (key, value) in textHeld) reading.TextHeld[key] = value;
            foreach (var (key, food) in judgedFoods) reading.JudgedFoods[key] = food;
            return reading;
        }

        internal static IEnumerable<long> ParseIds(string? ids) =>
            string.IsNullOrEmpty(ids) ? Enumerable.Empty<long>() : ids.Split(',').Select(s => long.Parse(s, CultureInfo.InvariantCulture));
    }

    /// <summary>One restriction set of the active build, counted from the stored rows: <c>all</c> criteria or the <c>severe</c> ones.</summary>
    /// <param name="Foods">The foods the set counts.</param>
    /// <param name="RecipesByFood">The live decided recipes flagged by a line's food.</param>
    /// <param name="RecipesByText">The live decided recipes flagged as a whole: a line's own text, a drink line, retained alcohol.</param>
    /// <param name="RecipesUnknown">The live decided recipes the set could not decide (no ingredient line, a food not decided).</param>
    /// <param name="RecipesNotDecided">The live recipes the build has not decided yet (new, or lines written since), held by every set.</param>
    /// <param name="RecipesHeld">What the set holds: the flagged and the not-decided recipes, and for a severe set the unknown ones too.</param>
    /// <param name="RecipesHeldOnlyForUnknown">Of those, the ones held only because they are unknown or not decided: the price of the safe side.</param>
    /// <param name="RecipesNoLine">Of the unknown recipes, those with no ingredient line the set can read.</param>
    /// <param name="RecipesUnknownFood">Of the unknown recipes, those with a line on a food the build has not decided.</param>
    /// <param name="RecipesByTitle">Of the unknown recipes, those a book recipe's title holds (<see cref="DietFlagReason.Title"/>).</param>
    /// <param name="RecipesByMethod">Of the unknown recipes, those a book recipe's method holds (<see cref="DietFlagReason.Method"/>).</param>
    /// <param name="RecipesUnknownWord">Of the unknown recipes, those with a line no curated food decides that names a word the set does not understand (<see cref="DietFlagReason.UnknownWord"/>): the work list the wording and link lanes shrink.</param>
    /// <param name="RecipesByFoodWord">Of the flagged recipes, those a line's word flags that the curated catalog knows only on foods the set flags, or a processing kind (<see cref="DietFlagReason.FoodWord"/>).</param>
    /// <param name="RecipesByReferral">Of the unknown recipes, those a line sends to another recipe or page of the book (<see cref="DietFlagReason.Referral"/>).</param>
    /// <param name="RecipesNotReadBySecondReader">Of the unknown recipes, those the rules find clear and the second reader's pair has not read yet (<see cref="DietFlagReason.SecondUnread"/>, while its gate setting is on).</param>
    /// <param name="RecipesHeldBySecondReader">Of the unknown recipes, those the rules find clear and a stored reading of the pair does not call clearly free (<see cref="DietFlagReason.SecondReader"/>).</param>
    /// <param name="RecipesJudged">Of the flagged recipes, those people judged to hold a food of the type while the rules do not flag them (<see cref="DietFlagReason.Judged"/>).</param>
    public sealed record DietStoredTypeCount(long TypeId, string Name, string Reading, int Severity, bool Severe, long SetId, string State, int Foods, int RecipesByFood, int RecipesByText, int RecipesUnknown, int RecipesNotDecided, int RecipesHeld, int RecipesHeldOnlyForUnknown,
        int RecipesNoLine, int RecipesUnknownFood, int RecipesByTitle, int RecipesByMethod, int RecipesUnknownWord = 0, int RecipesByFoodWord = 0, int RecipesByReferral = 0, int RecipesNotReadBySecondReader = 0, int RecipesHeldBySecondReader = 0, int RecipesJudged = 0);

    /// <summary>The count of every set of the active build over the live recipes, with the build it was read from; <see cref="Types"/> is empty before the first build is complete.</summary>
    public sealed record DietStoredCountsReport(int Recipes, long? BuildId, string? Version, DateTime? CompletedAt, DateTime? LastCycleAt, IReadOnlyList<DietStoredTypeCount> Types);

    /// <summary>Per set of a build: what it holds and how long it took to build.</summary>
    public sealed record DietGateSetReport(long SetId, long TypeId, string Name, string Reading, bool Severe, string State, DateTime? BuiltAt, double? FoodSeconds, double? RecipeSeconds, int Foods, int RecipesContaining, int RecipesUnknown);

    /// <summary>A build of the store: its version, state, timings and last cycle, and its sets.</summary>
    public sealed record DietGateBuildReport(long Id, string Version, string State, DateTime StartedAt, DateTime? CompletedAt, double? FoodSeconds, double? RecipeSeconds, double? TotalSeconds,
        int Foods, int Recipes, int Sets, DateTime? LastCycleAt, double? LastCycleSeconds, int LastCycleFoods, int LastCycleRecipes, string? Note, IReadOnlyList<DietGateSetReport> SetReports);

    /// <summary>The operator's reading of the store: the counts per set (<c>GET /api/DietAdmin/gate-counts</c>) and the builds with their timings (<c>GET /api/DietAdmin/gate-build</c>), all from stored rows.</summary>
    public static class DietGateStoreReports
    {
        /// <summary>The count of every set of the active build, in seconds and with no probe.</summary>
        public static async Task<DietStoredCountsReport> CountsAsync(ApplicationDbContext db)
        {
            var live = await db.Recipes.AsNoTracking().CountAsync();
            var build = await db.DietGateBuilds.AsNoTracking().Where(b => b.State == DietGateStore.Active).OrderByDescending(b => b.Id).FirstOrDefaultAsync();
            if (build == null) return new DietStoredCountsReport(live, null, null, null, null, Array.Empty<DietStoredTypeCount>());
            var sets = await db.DietGateSets.AsNoTracking().Where(s => s.BuildId == build.Id).ToListAsync();
            var typeIds = sets.Select(s => s.RestrictionTypeId).Distinct().ToList();
            var names = await db.Set<ReferenceEntity>().AsNoTracking().Where(r => typeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
            var severities = (await db.Set<RestrictionCriterionEntity>().AsNoTracking().Where(c => typeIds.Contains(c.RestrictionTypeId)).Select(c => new { c.RestrictionTypeId, c.Severity }).ToListAsync())
                .GroupBy(c => c.RestrictionTypeId).ToDictionary(g => g.Key, g => g.Max(c => c.Severity));
            var setIds = sets.Select(s => s.Id).ToList();
            var foods = await db.DietFoodFacts.AsNoTracking().Where(f => setIds.Contains(f.SetId) && f.Verdict == DietVerdict.Contains)
                .GroupBy(f => f.SetId).Select(g => new { SetId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.SetId, g => g.Count);
            var buildId = build.Id;
            var flags = await (from f in db.DietRecipeFlags.AsNoTracking()
                               join r in db.Recipes.AsNoTracking() on f.RecipeId equals r.Id
                               join s in db.DietRecipeStates.AsNoTracking().Standing(db) on new { f.RecipeId, BuildId = buildId } equals new { s.RecipeId, s.BuildId }
                               where setIds.Contains(f.SetId)
                               group f by new { f.SetId, f.Verdict, f.Reason } into g
                               select new { g.Key.SetId, g.Key.Verdict, g.Key.Reason, Count = g.Count() }).ToListAsync();
            var decided = await (from s in db.DietRecipeStates.AsNoTracking().Standing(db)
                                 join r in db.Recipes.AsNoTracking() on s.RecipeId equals r.Id
                                 where s.BuildId == buildId
                                 select s.RecipeId).CountAsync();
            var notDecided = Math.Max(0, live - decided);
            var types = new List<DietStoredTypeCount>();
            foreach (var set in sets.OrderBy(s => names.GetValueOrDefault(s.RestrictionTypeId), StringComparer.OrdinalIgnoreCase).ThenBy(s => s.RestrictionTypeId).ThenBy(s => s.MinimumSeverity))
            {
                var mine = flags.Where(f => f.SetId == set.Id).ToList();
                var byFood = mine.Where(f => f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.Food).Sum(f => f.Count);
                var byText = mine.Where(f => f.Verdict == DietVerdict.Contains && f.Reason != DietFlagReason.Food).Sum(f => f.Count);
                var unknown = mine.Where(f => f.Verdict == DietVerdict.Unknown).Sum(f => f.Count);
                var onlyUnknown = (set.Severe ? unknown : 0) + notDecided;
                types.Add(new DietStoredTypeCount(set.RestrictionTypeId, names.GetValueOrDefault(set.RestrictionTypeId) ?? "#" + set.RestrictionTypeId, set.MinimumSeverity >= 4 ? "severe" : "all",
                    severities.GetValueOrDefault(set.RestrictionTypeId), set.Severe, set.Id, set.State, foods.GetValueOrDefault(set.Id), byFood, byText, unknown, notDecided, byFood + byText + onlyUnknown, onlyUnknown,
                    Unknown(DietFlagReason.NoLine), Unknown(DietFlagReason.UnknownFood), Unknown(DietFlagReason.Title), Unknown(DietFlagReason.Method), Unknown(DietFlagReason.UnknownWord),
                    mine.Where(f => f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.FoodWord).Sum(f => f.Count), Unknown(DietFlagReason.Referral),
                    Unknown(DietFlagReason.SecondUnread), Unknown(DietFlagReason.SecondReader),
                    mine.Where(f => f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.Judged).Sum(f => f.Count)));

                int Unknown(DietFlagReason reason) => mine.Where(f => f.Verdict == DietVerdict.Unknown && f.Reason == reason).Sum(f => f.Count);
            }
            return new DietStoredCountsReport(live, build.Id, build.Version, build.CompletedAt, build.LastCycleAt, types);
        }

        /// <summary>The builds that are not retired, newest first, each with its sets' timings.</summary>
        public static async Task<List<DietGateBuildReport>> BuildsAsync(ApplicationDbContext db)
        {
            var builds = await db.DietGateBuilds.AsNoTracking().Where(b => b.State != DietGateStore.Retired).OrderByDescending(b => b.Id).ToListAsync();
            var ids = builds.Select(b => b.Id).ToList();
            var sets = await db.DietGateSets.AsNoTracking().Where(s => ids.Contains(s.BuildId)).ToListAsync();
            var typeIds = sets.Select(s => s.RestrictionTypeId).Distinct().ToList();
            var names = await db.Set<ReferenceEntity>().AsNoTracking().Where(r => typeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
            return builds.Select(b => new DietGateBuildReport(b.Id, b.Version, b.State, b.StartedAt, b.CompletedAt, b.FoodSeconds, b.RecipeSeconds, b.TotalSeconds, b.Foods, b.Recipes, b.Sets,
                    b.LastCycleAt, b.LastCycleSeconds, b.LastCycleFoods, b.LastCycleRecipes, b.Note,
                    sets.Where(s => s.BuildId == b.Id)
                        .OrderBy(s => names.GetValueOrDefault(s.RestrictionTypeId), StringComparer.OrdinalIgnoreCase).ThenBy(s => s.MinimumSeverity)
                        .Select(s => new DietGateSetReport(s.Id, s.RestrictionTypeId, names.GetValueOrDefault(s.RestrictionTypeId) ?? "#" + s.RestrictionTypeId, s.MinimumSeverity >= 4 ? "severe" : "all",
                            s.Severe, s.State, s.BuiltAt, s.FoodSeconds, s.RecipeSeconds, s.Foods, s.RecipesContaining, s.RecipesUnknown))
                        .ToList()))
                .ToList();
        }
    }
}
