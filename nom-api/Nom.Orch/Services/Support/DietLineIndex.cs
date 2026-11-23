using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The diet gate's reading of every live recipe line, kept in memory so that no household set ever probes the
    /// line table (staging checkpoint 4, P1: on the production-sized copy the joined line probe ran past the 30 s
    /// command timeout for Celiac, Wheat, Milk, Egg, Lactose, Vegetarian, Vegan and Paleo, so search, plans and the
    /// prepared check answered HTTP 400). For every pattern criterion of every restriction type (a name pattern read
    /// by <see cref="DietPatternRule"/>, keyed by its pattern and the type its exceptions are scoped to, or a word
    /// pattern) it keeps the recipes with a live line whose own text names it (<see cref="DietCriteriaMatcher.LineNames"/>),
    /// and beside them the lines whose own text reads as a drink (<see cref="DietCriteriaMatcher.LineCounts"/>) and the
    /// recipes on each food. A pass reads the lines in windows of recipe ids, one bounded query each, and reads a line
    /// for a rule only when it holds the rule's first word (a line with a character that a match without case may read
    /// as an ASCII letter is read for every rule), so the reading is the matcher's own. A recipe whose lines are as the last pass read them (the same
    /// fingerprint of foods, order and text) keeps its reading and is read only for the rules that are new, so a pass
    /// after a write, a rule change or a restart of the reading costs one read of the lines. Each rule knows up to which
    /// recipe id it has been read: a recipe above it (one made since, or every recipe before the first pass reaches it)
    /// is not yet read, and the gate holds it (<see cref="HouseholdRestrictionResolver"/>). The reading is published as an
    /// immutable <see cref="Snapshot"/> while a pass runs, so a reader never waits.
    /// </summary>
    public sealed class DietLineIndex
    {
        /// <summary>The index the API reads and its warm keeps current, keyed by database.</summary>
        public static readonly DietLineIndex Shared = new();

        /// <summary>How many recipe ids one read of a pass covers.</summary>
        public const int DefaultWindow = 2000;

        /// <summary>How often a running pass publishes what it has read so far.</summary>
        public static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(10);

        /// <summary>One live recipe line as a pass reads it.</summary>
        public sealed record Line(long RecipeId, long IngredientId, int SortOrder, string? RawLine);

        /// <summary>One rule a line is read for: its key, the first word a line must hold (lower case; null when every line is read) and the reading.</summary>
        public sealed class Rule
        {
            internal Rule(string key, string? seed, Func<string, bool> names)
            {
                Key = key;
                Seed = seed;
                Names = names;
            }

            public string Key { get; }

            public string? Seed { get; }

            internal Func<string, bool> Names { get; }
        }

        /// <summary>What a pass did.</summary>
        public sealed record PassReport(int Rules, int NewRules, int Recipes, int Read, int Lines, long ReadUpTo, TimeSpan Took);

        /// <summary>What a set of criteria reads from a snapshot: the recipes a line of which names a pattern the type reads in every line, the recipes a line of which names any of its patterns, the recipe id up to which every one of its rules has been read, and the keys of the rules not read at all.</summary>
        public sealed record View(IReadOnlySet<long> LineRecipes, IReadOnlySet<long> Named, long ReadUpTo, IReadOnlyList<string> Missing);

        private sealed record Entry(ulong Fingerprint, string[] Rules, DietCriteriaMatcher.RecipeLine[] Drinks, long[] Foods, int Lines);

        private sealed class State
        {
            public readonly SemaphoreSlim Pass = new(1, 1);
            public readonly Dictionary<long, Entry> Entries = new();
            public Dictionary<string, long> Coverage = new(StringComparer.Ordinal);
            public long DrinkCoverage;
            public Snapshot? Current;
            public int Requested;
        }

        private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);

        private static readonly Regex DrinkWord = DietCriteriaMatcher.WordRegex(AlcoholLineContext.DrinkNamePattern);

        /// <summary>The key a criterion's rule is kept under: a name pattern by its pattern and the type its exceptions are scoped to, a word pattern by itself; null for a nutrient criterion.</summary>
        public static string? KeyOf(DietDataCriterion criterion) =>
            criterion.Rule is { } rule ? "like:" + rule.Pattern + "\n" + rule.TypeName
            : criterion.WordPattern != null ? "word:" + criterion.WordPattern
            : null;

        /// <summary>The distinct rules of the pattern criteria.</summary>
        public static List<Rule> Rules(IEnumerable<DietDataCriterion> criteria)
        {
            var result = new Dictionary<string, Rule>(StringComparer.Ordinal);
            foreach (var c in criteria)
            {
                if (KeyOf(c) is not { } key || result.ContainsKey(key)) continue;
                if (c.Rule is { } rule) result[key] = new Rule(key, Seed(rule), rule.IsMatch);
                else
                {
                    var criterion = c;
                    result[key] = new Rule(key, null, text => DietCriteriaMatcher.LineNames(criterion, text));
                }
            }
            return result.Values.ToList();
        }

        /// <summary>The rules of every pattern criterion of every restriction type in the database.</summary>
        public static async Task<List<Rule>> RulesAsync(ApplicationDbContext db)
        {
            var typeIds = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Where(c => c.IngredientPattern != null)
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync();
            return Rules(await DietCriteriaMatcher.CriteriaAsync(db, typeIds));
        }

        private static string? Seed(DietPatternRule rule)
        {
            var seed = rule.Substring
                ? rule.Pattern.Split('%', '_', '\\').OrderByDescending(p => p.Length).FirstOrDefault()?.ToLowerInvariant()
                : rule.Words.Count > 0 ? rule.Words[0] : null;
            return string.IsNullOrEmpty(seed) || !IsAscii(seed) ? null : seed;
        }

        private static bool IsAscii(string text)
        {
            foreach (var ch in text)
                if (ch > '\u007f') return false;
            return true;
        }

        /// <summary>Whether the text holds a character beyond ASCII that a match without case may read as an ASCII letter (the long s, the Kelvin sign, the Turkish dotted and dotless i, or any whose case maps into ASCII); such a line is read for every rule.</summary>
        internal static bool FoldsToAscii(string text)
        {
            foreach (var ch in text)
            {
                if (ch <= '\u007f') continue;
                if (ch is 'ſ' or 'K' or 'İ' or 'ı' || char.ToLowerInvariant(ch) <= '\u007f' || char.ToUpperInvariant(ch) <= '\u007f') return true;
            }
            return false;
        }

        /// <summary>The snapshot published for the context's database, or null before the first pass has published one.</summary>
        public Snapshot? Current(ApplicationDbContext db) => Current(DietGateCache.DatabaseKey(db));

        /// <summary>The snapshot published for the database <paramref name="databaseKey"/>, or null before the first pass has published one.</summary>
        public Snapshot? Current(string databaseKey) => _states.TryGetValue(databaseKey, out var state) ? Volatile.Read(ref state.Current) : null;

        /// <summary>Asks the warm for a pass soon (a reader met a rule not read yet).</summary>
        public void Request(ApplicationDbContext db) => Interlocked.Exchange(ref _states.GetOrAdd(DietGateCache.DatabaseKey(db), _ => new State()).Requested, 1);

        /// <summary>Whether a pass was asked for since the last call, clearing the request.</summary>
        public bool TakeRequest(ApplicationDbContext db) => _states.TryGetValue(DietGateCache.DatabaseKey(db), out var state) && Interlocked.Exchange(ref state.Requested, 0) == 1;

        /// <summary>What one warm did: the food probes and retained counts computed, and the pass over the lines.</summary>
        public sealed record WarmReport(int FoodProbes, int Retained, PassReport Pass, TimeSpan Took);

        /// <summary>
        /// Warms the diet gate on the context's database: the food probe of every restriction type, or with
        /// <paramref name="everyType"/> false of the types a restriction names (<see cref="DietCriteriaMatcher.WarmFoodsAsync"/>),
        /// one pass over the lines, then the retained alcohol counted on the pass's drink lines
        /// (<see cref="DietCriteriaMatcher.WarmRetainedAsync"/>). Until each part is done, a household set that needs it
        /// holds what it cannot read yet.
        /// </summary>
        public async Task<WarmReport> WarmAsync(ApplicationDbContext db, DietGateCache cache, CancellationToken cancellationToken = default, int window = DefaultWindow, bool everyType = true)
        {
            var clock = Stopwatch.StartNew();
            var foods = await DietCriteriaMatcher.WarmFoodsAsync(db, cache, inUseOnly: !everyType);
            var pass = await RefreshAsync(db, cancellationToken, window);
            var retained = await DietCriteriaMatcher.WarmRetainedAsync(db, cache, this);
            return new WarmReport(foods, retained, pass, clock.Elapsed);
        }

        /// <summary>
        /// One pass over the context's database: reads the rules of every pattern criterion, then every live line in
        /// windows of <paramref name="window"/> recipe ids up to the highest recipe id at the start, and publishes the
        /// reading as it goes and at the end.
        /// </summary>
        public async Task<PassReport> RefreshAsync(ApplicationDbContext db, CancellationToken cancellationToken = default, int window = DefaultWindow)
        {
            var rules = await RulesAsync(db);
            var maxRecipe = await db.Recipes.IgnoreQueryFilters().AsNoTracking().MaxAsync(r => (long?)r.Id, cancellationToken) ?? 0;
            var maxLine = await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking().MaxAsync(ri => (long?)ri.RecipeId, cancellationToken) ?? 0;
            return await RefreshAsync(DietGateCache.DatabaseKey(db), rules, Math.Max(maxRecipe, maxLine),
                (after, upTo, ct) => db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(ri => !ri.IsDeleted && ri.RecipeId > after && ri.RecipeId <= upTo)
                    .Select(ri => new Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                    .ToListAsync(ct),
                cancellationToken, window);
        }

        /// <summary>One pass over lines read by <paramref name="source"/> (the lines of the recipes with an id above its first argument and up to its second), for the database <paramref name="databaseKey"/>.</summary>
        public async Task<PassReport> RefreshAsync(string databaseKey, IReadOnlyList<Rule> rules, long maxRecipeId,
            Func<long, long, CancellationToken, Task<List<Line>>> source, CancellationToken cancellationToken = default, int window = DefaultWindow)
        {
            var state = _states.GetOrAdd(databaseKey, _ => new State());
            await state.Pass.WaitAsync(cancellationToken);
            try
            {
                var clock = Stopwatch.StartNew();
                var keys = rules.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
                var fresh = rules.Where(r => !state.Coverage.ContainsKey(r.Key)).ToList();
                var all = new Matcher(rules);
                var readTo = rules.ToDictionary(r => r.Key, r => state.Coverage.TryGetValue(r.Key, out var c) ? c : -1L, StringComparer.Ordinal);
                var levels = readTo.Values.Distinct().OrderBy(v => v).ToArray();
                var pending = new ConcurrentDictionary<long, Matcher>();
                Matcher? PendingFor(long recipeId)
                {
                    var below = levels.Where(v => v < recipeId).DefaultIfEmpty(long.MinValue).Max();
                    return below == long.MinValue ? null : pending.GetOrAdd(below, level => new Matcher(rules.Where(r => readTo[r.Key] <= level).ToList()));
                }
                var coverage = state.Coverage.Where(kv => keys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                foreach (var r in fresh) coverage[r.Key] = 0;
                var drinkCoverage = state.DrinkCoverage;
                var read = 0;
                var lines = 0;
                var published = Stopwatch.StartNew();
                var step = Math.Max(1, window);
                for (var after = 0L; after < maxRecipeId; after += step)
                {
                    var upTo = Math.Min(after + step, maxRecipeId);
                    var batch = await source(after, upTo, cancellationToken);
                    lines += batch.Count;
                    var groups = batch.GroupBy(l => l.RecipeId).Select(g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.IngredientId).ToList()).ToList();
                    var entries = new Entry[groups.Count];
                    var reread = 0;
                    Parallel.For(0, groups.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
                    {
                        var recipe = groups[i];
                        var id = recipe[0].RecipeId;
                        var fingerprint = Fingerprint(recipe);
                        state.Entries.TryGetValue(id, out var previous);
                        if (previous != null && previous.Fingerprint == fingerprint)
                        {
                            var unread = PendingFor(id);
                            entries[i] = unread == null && previous.Rules.All(keys.Contains)
                                ? previous
                                : previous with { Rules = previous.Rules.Where(keys.Contains).Concat(unread?.Read(recipe, false).Rules ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray() };
                        }
                        else
                        {
                            var (matched, drinks) = all.Read(recipe, true);
                            entries[i] = new Entry(fingerprint, matched, drinks, recipe.Select(l => l.IngredientId).Distinct().ToArray(), recipe.Count);
                            Interlocked.Increment(ref reread);
                        }
                    });
                    read += reread;
                    var seen = new HashSet<long>();
                    for (var i = 0; i < groups.Count; i++)
                    {
                        state.Entries[groups[i][0].RecipeId] = entries[i];
                        seen.Add(groups[i][0].RecipeId);
                    }
                    foreach (var gone in state.Entries.Keys.Where(id => id > after && id <= upTo && !seen.Contains(id)).ToList()) state.Entries.Remove(gone);
                    foreach (var key in keys) coverage[key] = Math.Max(coverage[key], upTo);
                    drinkCoverage = Math.Max(drinkCoverage, upTo);
                    if (published.Elapsed >= PublishInterval && upTo < maxRecipeId)
                    {
                        Publish(state, coverage, drinkCoverage);
                        published.Restart();
                    }
                }
                foreach (var gone in state.Entries.Keys.Where(id => id > maxRecipeId).ToList()) state.Entries.Remove(gone);
                foreach (var key in keys) coverage[key] = Math.Max(coverage[key], maxRecipeId);
                drinkCoverage = Math.Max(drinkCoverage, maxRecipeId);
                Publish(state, coverage, drinkCoverage);
                return new PassReport(rules.Count, fresh.Count, state.Entries.Count, read, lines, maxRecipeId, clock.Elapsed);
            }
            finally
            {
                state.Pass.Release();
            }
        }

        private static void Publish(State state, Dictionary<string, long> coverage, long drinkCoverage)
        {
            var recipes = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            var foods = new Dictionary<long, List<long>>();
            var drinks = new List<DietCriteriaMatcher.RecipeLine>();
            var lines = 0;
            foreach (var (id, entry) in state.Entries)
            {
                lines += entry.Lines;
                foreach (var key in entry.Rules)
                {
                    if (!recipes.TryGetValue(key, out var set)) recipes[key] = set = new HashSet<long>();
                    set.Add(id);
                }
                foreach (var food in entry.Foods)
                {
                    if (!foods.TryGetValue(food, out var list)) foods[food] = list = new List<long>();
                    list.Add(id);
                }
                drinks.AddRange(entry.Drinks);
            }
            state.Coverage = new Dictionary<string, long>(coverage, StringComparer.Ordinal);
            state.DrinkCoverage = drinkCoverage;
            Volatile.Write(ref state.Current, new Snapshot(recipes, foods.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()), drinks,
                new Dictionary<string, long>(coverage, StringComparer.Ordinal), drinkCoverage, state.Entries.Count, lines, DateTime.UtcNow));
        }

        private static ulong Fingerprint(IReadOnlyList<Line> recipe)
        {
            const ulong prime = 1099511628211UL;
            var hash = 14695981039346656037UL;
            foreach (var line in recipe)
            {
                var food = (ulong)line.IngredientId;
                for (var b = 0; b < 8; b++)
                {
                    hash = (hash ^ (food & 0xff)) * prime;
                    food >>= 8;
                }
                foreach (var ch in line.RawLine ?? string.Empty)
                {
                    hash = (hash ^ (ulong)(ch & 0xff)) * prime;
                    hash = (hash ^ (ulong)(ch >> 8)) * prime;
                }
                hash = (hash ^ 0x1f) * prime;
            }
            return hash;
        }

        private sealed class Matcher
        {
            private readonly (string Seed, Rule[] Rules)[] _seeded;
            private readonly Rule[] _unseeded;
            private readonly Rule[] _all;

            public Matcher(IReadOnlyList<Rule> rules)
            {
                _all = rules.ToArray();
                _seeded = rules.Where(r => r.Seed != null).GroupBy(r => r.Seed!, StringComparer.Ordinal).Select(g => (g.Key, g.ToArray())).ToArray();
                _unseeded = rules.Where(r => r.Seed == null).ToArray();
            }

            public (string[] Rules, DietCriteriaMatcher.RecipeLine[] Drinks) Read(IReadOnlyList<Line> recipe, bool drinks)
            {
                var matched = new HashSet<string>(StringComparer.Ordinal);
                List<DietCriteriaMatcher.RecipeLine>? drinkLines = null;
                foreach (var line in recipe)
                {
                    var text = line.RawLine;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (_all.Length > 0)
                    {
                        if (FoldsToAscii(text))
                        {
                            foreach (var rule in _all)
                                if (!matched.Contains(rule.Key) && rule.Names(text)) matched.Add(rule.Key);
                        }
                        else
                        {
                            var lower = text.ToLowerInvariant();
                            foreach (var (seed, rules) in _seeded)
                            {
                                if (!lower.Contains(seed, StringComparison.Ordinal)) continue;
                                foreach (var rule in rules)
                                    if (!matched.Contains(rule.Key) && rule.Names(text)) matched.Add(rule.Key);
                            }
                            foreach (var rule in _unseeded)
                                if (!matched.Contains(rule.Key) && rule.Names(text)) matched.Add(rule.Key);
                        }
                    }
                    if (drinks && DrinkWord.IsMatch(text) && DietCriteriaMatcher.LineCounts(text))
                        (drinkLines ??= new()).Add(new DietCriteriaMatcher.RecipeLine(line.RecipeId, line.IngredientId, text));
                }
                return (matched.ToArray(), drinkLines?.ToArray() ?? Array.Empty<DietCriteriaMatcher.RecipeLine>());
            }
        }

        /// <summary>
        /// One published reading: for each rule the recipes a live line of which names it and the recipe id up to which
        /// it has been read, the lines that read as a drink, and the recipes on each food. Immutable; what a set of
        /// criteria reads from it is kept per set.
        /// </summary>
        public sealed class Snapshot
        {
            private static readonly HashSet<long> None = new();
            private readonly Dictionary<string, HashSet<long>> _recipes;
            private readonly Dictionary<long, long[]> _foods;
            private readonly Dictionary<string, long> _coverage;
            private readonly ConcurrentDictionary<string, View> _views = new(StringComparer.Ordinal);

            internal Snapshot(Dictionary<string, HashSet<long>> recipes, Dictionary<long, long[]> foods, List<DietCriteriaMatcher.RecipeLine> drinkLines,
                Dictionary<string, long> coverage, long drinkReadUpTo, int recipeCount, int lineCount, DateTime at)
            {
                _recipes = recipes;
                _foods = foods;
                _coverage = coverage;
                DrinkLines = drinkLines;
                DrinkReadUpTo = drinkReadUpTo;
                RecipeCount = recipeCount;
                LineCount = lineCount;
                At = at;
            }

            /// <summary>Every live line whose own text reads as a drink (<see cref="DietCriteriaMatcher.LineCounts"/>), whatever its food.</summary>
            public IReadOnlyList<DietCriteriaMatcher.RecipeLine> DrinkLines { get; }

            /// <summary>The recipe id up to which the drink lines have been read.</summary>
            public long DrinkReadUpTo { get; }

            /// <summary>How many recipes with a live line the snapshot holds.</summary>
            public int RecipeCount { get; }

            /// <summary>How many live lines the snapshot read.</summary>
            public int LineCount { get; }

            /// <summary>When the snapshot was published.</summary>
            public DateTime At { get; }

            /// <summary>How many rules the snapshot has read, and the lowest recipe id up to which one has been read.</summary>
            public (int Rules, long ReadUpTo) Coverage => (_coverage.Count, _coverage.Count == 0 ? DrinkReadUpTo : Math.Min(DrinkReadUpTo, _coverage.Values.Min()));

            /// <summary>The recipes with a live line whose own text names the rule kept under <paramref name="key"/>.</summary>
            public IReadOnlySet<long> RecipesNaming(string key) => _recipes.TryGetValue(key, out var set) ? set : None;

            /// <summary>The recipes with a live line on one of the foods.</summary>
            public HashSet<long> RecipesOn(IEnumerable<long> foodIds)
            {
                var result = new HashSet<long>();
                foreach (var food in foodIds)
                    if (_foods.TryGetValue(food, out var ids)) result.UnionWith(ids);
                return result;
            }

            /// <summary>What one restriction type's criteria read: <see cref="View.LineRecipes"/> by the patterns the type reads in every line (<see cref="DietCriteriaMatcher.LinePatterns"/>), <see cref="View.Named"/> by every pattern, and the lowest recipe id up to which they have all been read (0 when one has not been read at all).</summary>
            public View ViewOf(IReadOnlyCollection<DietDataCriterion> criteria)
            {
                var line = DietCriteriaMatcher.LinePatterns(criteria).Select(KeyOf).OfType<string>().Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var any = criteria.Select(KeyOf).OfType<string>().Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
                return _views.GetOrAdd(string.Join('\u0001', line) + '\u0002' + string.Join('\u0001', any), _ =>
                {
                    var missing = any.Where(k => !_coverage.ContainsKey(k)).ToList();
                    var lineRecipes = new HashSet<long>();
                    foreach (var key in line) lineRecipes.UnionWith(RecipesNaming(key));
                    var named = new HashSet<long>(lineRecipes);
                    foreach (var key in any.Except(line)) named.UnionWith(RecipesNaming(key));
                    var upTo = missing.Count > 0 ? 0 : any.Count == 0 ? long.MaxValue : any.Min(k => _coverage[k]);
                    return new View(lineRecipes, named, upTo, missing);
                });
            }
        }
    }
}
