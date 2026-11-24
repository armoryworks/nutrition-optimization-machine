using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The food catalog as the diet gate's food decision reads it, loaded once by the build job (staging checkpoint 5,
    /// Q2): every food with its name, liveness, USDA category and weights, every live alias, the live rows of the
    /// criteria's nutrients, and the latest live FDC ingredient statement of each food (its text loaded only for the
    /// foods being decided). Each food has a fingerprint of everything its decision and its recipes' retained alcohol
    /// read, so the build job decides a food again only when it changes.
    /// </summary>
    public sealed class DietFoodCatalog
    {
        /// <summary>One food as the decision reads it.</summary>
        public sealed record Food(long Id, string Name, bool Live, int? CategoryId, string? DataType, string? Category, decimal? GramsPerMilliliter, decimal? GramsPerItem, decimal? ReferenceServingGrams);

        /// <summary>One live alias.</summary>
        public sealed record Alias(long IngredientId, string Text, string? Context);

        /// <summary>The latest live FDC statement row of a food, without its text.</summary>
        public sealed record Statement(long Id, long IngredientId, string Outcome, string ReaderVersion, DateTime? Modified);

        private DietFoodCatalog(Dictionary<long, Food> foods, Dictionary<long, Alias[]> aliases, Dictionary<long, Dictionary<long, decimal>> nutrients, Dictionary<long, Statement> statements)
        {
            Foods = foods;
            Aliases = aliases;
            Nutrients = nutrients;
            Statements = statements;
        }

        public IReadOnlyDictionary<long, Food> Foods { get; }

        /// <summary>The live aliases of each food, of a deleted food too (the gate's alias probe never joined the food).</summary>
        public IReadOnlyDictionary<long, Alias[]> Aliases { get; }

        /// <summary>Per food, the highest amount per 100 g of each criterion nutrient among its live rows; a food with a row of a nutrient stores it.</summary>
        public IReadOnlyDictionary<long, Dictionary<long, decimal>> Nutrients { get; }

        /// <summary>The latest live FDC statement row of each food that has one.</summary>
        public IReadOnlyDictionary<long, Statement> Statements { get; }

        /// <summary>The statement rows with their text, for the foods <see cref="LoadStatementsAsync"/> loaded.</summary>
        public Dictionary<long, IngredientContentsEntity> StatementRows { get; } = new();

        /// <summary>Reads the catalog; the statements' text is not read here.</summary>
        public static async Task<DietFoodCatalog> LoadAsync(ApplicationDbContext db, IReadOnlyCollection<long> nutrientIds, CancellationToken cancellationToken = default)
        {
            var foods = (await db.Ingredients.IgnoreQueryFilters().AsNoTracking()
                    .Select(i => new { i.Id, i.Name, i.IsDeleted, i.FdcCategoryId, i.FdcDataType, i.FdcCategory, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams })
                    .ToListAsync(cancellationToken))
                .ToDictionary(i => i.Id, i => new Food(i.Id, i.Name ?? string.Empty, !i.IsDeleted, i.FdcCategoryId, i.FdcDataType, i.FdcCategory, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams));
            var aliases = (await db.IngredientAliases.AsNoTracking()
                    .Select(a => new { a.IngredientId, a.AliasName, a.SourceContext })
                    .ToListAsync(cancellationToken))
                .GroupBy(a => a.IngredientId)
                .ToDictionary(g => g.Key, g => g.Select(a => new Alias(a.IngredientId, a.AliasName ?? string.Empty, a.SourceContext)).ToArray());
            var ids = nutrientIds.Distinct().ToList();
            var nutrients = ids.Count == 0 ? new Dictionary<long, Dictionary<long, decimal>>() : (await db.IngredientNutrients.AsNoTracking()
                    .Where(n => ids.Contains(n.NutrientId))
                    .Select(n => new { n.IngredientId, n.NutrientId, n.Amount })
                    .ToListAsync(cancellationToken))
                .GroupBy(n => n.IngredientId)
                .ToDictionary(g => g.Key, g => g.GroupBy(n => n.NutrientId).ToDictionary(n => n.Key, n => n.Max(x => x.Amount)));
            var statements = (await db.IngredientContents.AsNoTracking()
                    .Where(c => c.HouseholdId == null)
                    .Select(c => new { c.Id, c.IngredientId, c.Outcome, c.ReaderVersion, c.LastModifiedDate })
                    .ToListAsync(cancellationToken))
                .GroupBy(c => c.IngredientId)
                .Select(g => g.OrderByDescending(c => c.Id).First())
                .ToDictionary(c => c.IngredientId, c => new Statement(c.Id, c.IngredientId, c.Outcome, c.ReaderVersion, c.LastModifiedDate));
            return new DietFoodCatalog(foods, aliases, nutrients, statements);
        }

        /// <summary>Loads the text of the latest statement of each of the foods that has a read one, in bounded batches.</summary>
        public async Task LoadStatementsAsync(ApplicationDbContext db, IEnumerable<long> foodIds, CancellationToken cancellationToken = default)
        {
            var rowIds = foodIds.Where(id => !StatementRows.ContainsKey(id) && Statements.TryGetValue(id, out var s) && s.Outcome == IngredientContentsOutcome.Read)
                .Select(id => Statements[id].Id)
                .ToList();
            foreach (var batch in rowIds.Chunk(5000))
            {
                var rows = await db.IngredientContents.AsNoTracking()
                    .Where(c => batch.Contains(c.Id))
                    .Select(c => new { c.Id, c.IngredientId, c.Outcome, c.IngredientsJson, c.AllergensJson })
                    .ToListAsync(cancellationToken);
                foreach (var c in rows)
                    StatementRows[c.IngredientId] = new IngredientContentsEntity { Id = c.Id, IngredientId = c.IngredientId, Outcome = c.Outcome, IngredientsJson = c.IngredientsJson, AllergensJson = c.AllergensJson };
            }
        }

        /// <summary>The fingerprint of everything the decision of a food and its recipes' retained alcohol read; 0 for a food that is not in the catalog.</summary>
        public long Fingerprint(long foodId)
        {
            if (!Foods.TryGetValue(foodId, out var food)) return 0;
            var hash = new Fnv();
            hash.Add(food.Name).Add(food.Live ? "1" : "0").Add(food.CategoryId).Add(food.DataType).Add(food.Category)
                .Add(food.GramsPerMilliliter).Add(food.GramsPerItem).Add(food.ReferenceServingGrams);
            if (Aliases.TryGetValue(foodId, out var aliases))
                foreach (var a in aliases.OrderBy(a => a.Text, StringComparer.Ordinal).ThenBy(a => a.Context, StringComparer.Ordinal)) hash.Add(a.Text).Add(a.Context);
            hash.Add("|");
            if (Nutrients.TryGetValue(foodId, out var nutrients))
                foreach (var (id, amount) in nutrients.OrderBy(n => n.Key)) hash.Add(id).Add(amount);
            hash.Add("|");
            if (Statements.TryGetValue(foodId, out var s)) hash.Add(s.Id).Add(s.Outcome).Add(s.ReaderVersion).Add(s.Modified?.Ticks);
            return hash.Value;
        }

        internal sealed class Fnv
        {
            private ulong _hash = 14695981039346656037UL;

            public long Value => unchecked((long)_hash);

            public Fnv Add(string? text)
            {
                foreach (var ch in text ?? "\u0000")
                {
                    _hash = (_hash ^ (ulong)(ch & 0xff)) * 1099511628211UL;
                    _hash = (_hash ^ (ulong)(ch >> 8)) * 1099511628211UL;
                }
                _hash = (_hash ^ 0x1f) * 1099511628211UL;
                return this;
            }

            public Fnv Add(long? value) => Add(value?.ToString(CultureInfo.InvariantCulture));

            public Fnv Add(decimal? value) => Add(value?.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The diet gate's food decision in the build job (staging checkpoint 5, Q2): which foods each criterion of a
    /// restriction set counts, and which a pattern of it matches only through an alias that does not count, read from a
    /// loaded <see cref="DietFoodCatalog"/> for a batch of foods with no catalog probe. It reads as the matcher's food
    /// probe does (<see cref="DietCriteriaMatcher.ProbeAsync"/>): a nutrient criterion counts a live food storing the
    /// nutrient above <see cref="DietCriteriaMatcher.PresencePer100g"/> (for alcohol, no flavouring and no USDA spice,
    /// and a live food with no alcohol figure whose own name reads as a drink); a capped alcohol criterion counts only the
    /// latter; a word pattern counts a live food by its name and any food by a trusted alias with no alcohol word, its
    /// measures blanked, unless the food stores a nutrient of the set; a name pattern counts a live food by its name
    /// (<see cref="DietPatternRule.IsMatch"/>), any food by a trusted alias (<see cref="DietPatternRule.IsAliasMatch"/>,
    /// measures blanked), and a live food by its USDA category (<see cref="DietCriteriaMatcher.CategoryPatterns"/>); and a
    /// pattern counts a live food that is not a prepared meal by its latest read FDC statement
    /// (<see cref="PreparedContentsGate.RestrictedNames"/>). Each food is decided from its own data only, so a batch of
    /// changed foods is decided exactly as the whole catalog would decide them.
    /// </summary>
    public static class DietFoodReader
    {
        /// <summary>A set's decision: the foods each criterion counts (by criterion id), how each counts it, and the foods a pattern matches only through an alias that does not count.</summary>
        public sealed record Decision(Dictionary<long, HashSet<long>> Foods, Dictionary<(long Criterion, long Food), string> Sources, HashSet<long> AliasOnly);

        /// <summary>What a batch of foods reads for every rule, computed once per rule and kept for every set that needs it.</summary>
        public sealed class Reading
        {
            private readonly DietFoodCatalog.Food[] _live;
            private readonly string[] _liveLower;
            private readonly bool[] _liveFolds;
            private readonly DietFoodCatalog.Alias[] _aliases;
            private readonly string[] _blanked;
            private readonly string[] _blankedLower;
            private readonly string[] _rawLower;
            private readonly bool[] _aliasFolds;
            private readonly Dictionary<string, (HashSet<long> Names, List<int> Aliases)> _rules = new(StringComparer.Ordinal);
            private readonly Dictionary<string, (HashSet<long> Names, HashSet<int> Raw, HashSet<int> Blanked)> _words = new(StringComparer.Ordinal);
            private readonly Dictionary<string, HashSet<long>> _contents = new(StringComparer.Ordinal);
            private readonly Dictionary<long, string> _contentsWords = new();
            private HashSet<long>? _drinkNames;
            private bool[]? _nameCounts;
            private bool[]? _aliasCounts;

            internal Reading(DietFoodCatalog catalog, IReadOnlyCollection<long> foodIds)
            {
                Catalog = catalog;
                FoodIds = foodIds as IReadOnlySet<long> ?? foodIds.ToHashSet();
                _live = FoodIds.Select(id => catalog.Foods.TryGetValue(id, out var f) ? f : null).OfType<DietFoodCatalog.Food>().Where(f => f.Live).ToArray();
                _liveLower = _live.Select(f => f.Name.ToLowerInvariant()).ToArray();
                _liveFolds = _live.Select(f => DietLineIndex.FoldsToAscii(f.Name)).ToArray();
                _aliases = FoodIds.SelectMany(id => catalog.Aliases.TryGetValue(id, out var a) ? a : Array.Empty<DietFoodCatalog.Alias>()).ToArray();
                _blanked = _aliases.Select(a => DietCriteriaMatcher.AliasText(a.Text)).ToArray();
                _blankedLower = _blanked.Select(t => t.ToLowerInvariant()).ToArray();
                _rawLower = _aliases.Select(a => a.Text.ToLowerInvariant()).ToArray();
                _aliasFolds = _aliases.Select((a, i) => DietLineIndex.FoldsToAscii(a.Text) || DietLineIndex.FoldsToAscii(_blanked[i])).ToArray();
            }

            public DietFoodCatalog Catalog { get; }

            public IReadOnlySet<long> FoodIds { get; }

            internal IReadOnlyList<DietFoodCatalog.Food> Live => _live;

            internal (HashSet<long> Names, List<int> Aliases) Rule(DietPatternRule rule)
            {
                var key = "like:" + rule.Pattern + "\n" + rule.TypeName;
                if (_rules.TryGetValue(key, out var hit)) return hit;
                var seed = DietLineIndex.Seed(rule);
                var names = new HashSet<long>();
                for (var i = 0; i < _live.Length; i++)
                {
                    if (seed != null && !_liveFolds[i] && !_liveLower[i].Contains(seed, StringComparison.Ordinal)) continue;
                    if (rule.IsMatch(_live[i].Name)) names.Add(_live[i].Id);
                }
                var aliases = new List<int>();
                for (var i = 0; i < _aliases.Length; i++)
                {
                    if (seed != null && !_aliasFolds[i] && !_blankedLower[i].Contains(seed, StringComparison.Ordinal)) continue;
                    var match = rule.Substring
                        ? (_aliasFolds[i] || seed == null || _rawLower[i].Contains(seed, StringComparison.Ordinal)) && DietCriteriaMatcher.LikeRegex(rule.Pattern).IsMatch(_aliases[i].Text) && rule.IsMatch(_blanked[i])
                        : rule.IsAliasMatch(_blanked[i], _aliases[i].Context);
                    if (match) aliases.Add(i);
                }
                hit = (names, aliases);
                _rules[key] = hit;
                return hit;
            }

            internal (HashSet<long> Names, HashSet<int> Raw, HashSet<int> Blanked) Word(string pattern)
            {
                if (_words.TryGetValue(pattern, out var hit)) return hit;
                var regex = DietCriteriaMatcher.WordRegex(pattern);
                var names = new HashSet<long>();
                for (var i = 0; i < _live.Length; i++)
                    if (regex.IsMatch(_live[i].Name)) names.Add(_live[i].Id);
                var raw = new HashSet<int>();
                var blanked = new HashSet<int>();
                for (var i = 0; i < _aliases.Length; i++)
                {
                    if (regex.IsMatch(_aliases[i].Text)) raw.Add(i);
                    if (regex.IsMatch(_blanked[i])) blanked.Add(i);
                }
                hit = (names, raw, blanked);
                _words[pattern] = hit;
                return hit;
            }

            internal HashSet<long> DrinkNames()
            {
                if (_drinkNames != null) return _drinkNames;
                var regex = DietCriteriaMatcher.WordRegex(AlcoholLineContext.DrinkNamePattern);
                _drinkNames = _live.Where(f => regex.IsMatch(f.Name) && AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(f.Name))).Select(f => f.Id).ToHashSet();
                return _drinkNames;
            }

            internal bool NameCounts(int liveIndex) => (_nameCounts ??= _live.Select(f => DietCriteriaMatcher.NameCounts(f.Name)).ToArray())[liveIndex];

            internal bool AliasCounts(int aliasIndex) => (_aliasCounts ??= _aliases.Select((a, i) => DietCriteriaMatcher.AliasSourceCounts(a.Context) && DietCriteriaMatcher.AliasCounts(_blanked[i])).ToArray())[aliasIndex];

            internal DietFoodCatalog.Alias AliasAt(int index) => _aliases[index];

            internal int LiveIndex(long foodId)
            {
                _liveIndex ??= _live.Select((f, i) => (f.Id, i)).ToDictionary(x => x.Id, x => x.i);
                return _liveIndex.TryGetValue(foodId, out var i) ? i : -1;
            }

            private Dictionary<long, int>? _liveIndex;

            internal HashSet<long> Contents(DietDataCriterion criterion)
            {
                var key = (DietLineIndex.KeyOf(criterion) ?? string.Empty) + "\u0001" + (criterion.Severity >= 4 ? "4" : "0");
                if (_contents.TryGetValue(key, out var hit)) return hit;
                var set = new RestrictedIngredientSet();
                set.Criteria.Add(criterion);
                var seed = criterion.Rule is { Substring: false } rule ? rule.Words[0] : null;
                hit = new HashSet<long>();
                foreach (var food in _live)
                {
                    if (!Catalog.StatementRows.TryGetValue(food.Id, out var row) || row.Outcome != IngredientContentsOutcome.Read) continue;
                    if (PreparedFoods.IsPrepared(food.CategoryId, food.DataType, food.Category)) continue;
                    if (!_contentsWords.TryGetValue(food.Id, out var words)) _contentsWords[food.Id] = words = DietCriteriaMatcher.ContentsWords(row);
                    if (seed != null && !words.Contains(seed, StringComparison.Ordinal)) continue;
                    if (PreparedContentsGate.RestrictedNames(row, set).Any()) hit.Add(food.Id);
                }
                _contents[key] = hit;
                return hit;
            }
        }

        /// <summary>Prepares the reading of a batch of foods (the statements' text of the batch must be loaded for the contents to be read).</summary>
        public static Reading Read(DietFoodCatalog catalog, IReadOnlyCollection<long> foodIds) => new(catalog, foodIds);

        /// <summary>Decides the batch for one restriction set's criteria.</summary>
        public static Decision Decide(Reading reading, IReadOnlyList<DietDataCriterion> criteria)
        {
            var catalog = reading.Catalog;
            var result = new Dictionary<long, HashSet<long>>();
            var sources = new Dictionary<(long, long), string>();
            var aliasOnly = new HashSet<long>();
            void Count(long criterion, long food, string source)
            {
                if (!result.TryGetValue(criterion, out var set)) result[criterion] = set = new HashSet<long>();
                set.Add(food);
                sources.TryAdd((criterion, food), source);
            }
            bool Stores(long food, long nutrientId) => catalog.Nutrients.TryGetValue(food, out var n) && n.ContainsKey(nutrientId);
            HashSet<long> NoFigureDrinks(long nutrientId) => reading.DrinkNames().Where(id => !Stores(id, nutrientId)).ToHashSet();

            foreach (var c in criteria.Where(c => c.NutrientId != null && c.MaxAmountPerServing == null))
            {
                var nutrientId = c.NutrientId!.Value;
                result[c.Id] = new HashSet<long>();
                foreach (var food in reading.Live)
                {
                    if (!catalog.Nutrients.TryGetValue(food.Id, out var n) || !n.TryGetValue(nutrientId, out var amount) || amount <= DietCriteriaMatcher.PresencePer100g) continue;
                    if (DietCriteriaMatcher.IsAlcohol(c.NutrientName) && (food.CategoryId == CatalogAdmissionRule.SpicesAndHerbs || AlcoholLineContext.IsFlavouring(AlcoholLineContext.Read(food.Name)))) continue;
                    Count(c.Id, food.Id, "nutrient");
                }
                if (DietCriteriaMatcher.IsAlcohol(c.NutrientName))
                    foreach (var food in NoFigureDrinks(nutrientId)) Count(c.Id, food, "drink-name");
            }
            if (DietCriteriaMatcher.RetainedCriterion(criteria) is { } capped)
            {
                result[capped.Id] = new HashSet<long>();
                foreach (var food in NoFigureDrinks(capped.NutrientId!.Value)) Count(capped.Id, food, "drink-name");
            }

            var words = criteria.Where(c => c.WordPattern != null).ToList();
            if (words.Count > 0)
            {
                var hits = words.Select(c => reading.Word(c.WordPattern!)).ToList();
                var names = hits.SelectMany(h => h.Names).ToHashSet();
                var raw = hits.SelectMany(h => h.Raw).ToHashSet();
                var aliases = hits.SelectMany(h => h.Blanked).Where(raw.Contains).ToHashSet();
                var nutrientIds = criteria.Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).Distinct().ToList();
                var namedNames = names.Where(id => reading.NameCounts(reading.LiveIndex(id))).ToHashSet();
                var namedAliases = aliases.Where(reading.AliasCounts).ToHashSet();
                var decided = namedNames.Concat(namedAliases.Select(i => reading.AliasAt(i).IngredientId)).Where(id => nutrientIds.Any(n => Stores(id, n))).ToHashSet();
                foreach (var (c, hit) in words.Zip(hits))
                {
                    result[c.Id] = new HashSet<long>();
                    foreach (var id in hit.Names.Where(namedNames.Contains).Where(id => !decided.Contains(id))) Count(c.Id, id, "name");
                    foreach (var i in hit.Blanked.Where(namedAliases.Contains))
                    {
                        var id = reading.AliasAt(i).IngredientId;
                        if (!decided.Contains(id)) Count(c.Id, id, "alias");
                    }
                }
                aliasOnly.UnionWith(aliases.Where(i => !reading.AliasCounts(i)).Select(i => reading.AliasAt(i).IngredientId));
            }

            foreach (var c in criteria.Where(c => c.LikePattern != null))
            {
                var rule = c.Rule!;
                var (names, aliasHits) = reading.Rule(rule);
                result[c.Id] = new HashSet<long>();
                foreach (var id in names) Count(c.Id, id, "name");
                foreach (var i in aliasHits)
                {
                    var alias = reading.AliasAt(i);
                    if (DietCriteriaMatcher.AliasSourceCounts(alias.Context)) Count(c.Id, alias.IngredientId, "alias");
                }
                if (DietCriteriaMatcher.CategoryPatterns.TryGetValue(rule.Pattern, out var categories))
                    foreach (var food in reading.Live.Where(f => f.CategoryId is int id && categories.Contains(id))) Count(c.Id, food.Id, "category");
                var counted = result[c.Id];
                aliasOnly.UnionWith(aliasHits.Select(i => reading.AliasAt(i).IngredientId).Where(id => !counted.Contains(id)));
            }

            foreach (var c in criteria.Where(c => c.LikePattern != null || c.WordPattern != null))
                foreach (var food in reading.Contents(c)) Count(c.Id, food, "contents");

            foreach (var ids in result.Values) aliasOnly.ExceptWith(ids);
            return new Decision(result, sources, aliasOnly);
        }
    }
}
