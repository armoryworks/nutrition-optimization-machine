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
        /// <summary>One food as the decision reads it, and whether a person or the admission rule curated it (a curated food decides its lines; a line on any other food is read by its words, <see cref="DietFoodWords"/>).</summary>
        public sealed record Food(long Id, string Name, bool Live, int? CategoryId, string? DataType, string? Category, decimal? GramsPerMilliliter, decimal? GramsPerItem, decimal? ReferenceServingGrams, bool Curated = false, string? FdcId = null);

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
            _unreadByCategory = new(() => BrandedConsensus.UnreadByCategoryOf(Consensus));
        }

        public IReadOnlyDictionary<long, Food> Foods { get; }

        /// <summary>The live aliases of each food, of a deleted food too (the gate's alias probe never joined the food).</summary>
        public IReadOnlyDictionary<long, Alias[]> Aliases { get; }

        /// <summary>Per food, the highest amount per 100 g of each criterion nutrient among its live rows; a food with a row of a nutrient stores it.</summary>
        public IReadOnlyDictionary<long, Dictionary<long, decimal>> Nutrients { get; }

        /// <summary>The latest live FDC statement row of each food that has one.</summary>
        public IReadOnlyDictionary<long, Statement> Statements { get; }

        /// <summary>The rows of the branded consensus the decision reads: the committed file (<see cref="BrandedConsensus.All"/>), or the rows a test gives.</summary>
        public IReadOnlyList<ConsensusRow> Consensus { get; init; } = BrandedConsensus.All;

        /// <summary>The consensus rows of a food: only a curated food carries them, and only under the name they were derived for; a food of a made category with no FDC id takes the rows of its name whatever food they were derived for (Corn Tortillas is split on wheat, as the real food is).</summary>
        public IReadOnlyList<ConsensusRow> ConsensusOf(Food food) =>
            !food.Curated ? Array.Empty<ConsensusRow>() : BrandedConsensus.ForFood(food.Name, food.FdcId, ReferenceEquals(Consensus, BrandedConsensus.All) ? null : Consensus, anyFdc: NoFdcMade(food));

        private static bool NoFdcMade(Food food) => string.IsNullOrWhiteSpace(food.FdcId) && food.CategoryId is { } c && BrandedConsensus.MadeCategories.Contains(c);

        /// <summary>The classes a curated food of a made category with no consensus row of its own is of unknown contents for (<see cref="BrandedConsensus.UnreadClasses"/>), read from the consensus the catalog reads; none for a food with a row, an uncurated food or a food of another category. A food with no FDC id is unread only where no row of its name exists (<see cref="ConsensusOf"/>).</summary>
        public IReadOnlySet<string> UnreadClassesOf(Food food) =>
            !food.Curated || ConsensusOf(food).Count > 0 ? NoClasses : BrandedConsensus.UnreadClasses(food.CategoryId, ReferenceEquals(Consensus, BrandedConsensus.All) ? null : _unreadByCategory.Value);

        private static readonly IReadOnlySet<string> NoClasses = new HashSet<string>(StringComparer.Ordinal);

        private readonly Lazy<Dictionary<int, HashSet<string>>> _unreadByCategory;

        /// <summary>The statement rows with their text, for the foods <see cref="LoadStatementsAsync"/> loaded.</summary>
        public Dictionary<long, IngredientContentsEntity> StatementRows { get; } = new();

        /// <summary>Reads the catalog, or with <paramref name="foodIds"/> only those foods; the statements' text is not read here, and with <paramref name="withStatements"/> false not even their rows (a reader whose role may not read the contents table).</summary>
        public static async Task<DietFoodCatalog> LoadAsync(ApplicationDbContext db, IReadOnlyCollection<long> nutrientIds, CancellationToken cancellationToken = default, IReadOnlyCollection<long>? foodIds = null, bool withStatements = true)
        {
            var scopes = foodIds == null ? new long[]?[] { null } : foodIds.Distinct().Chunk(5000).Select(c => (long[]?)c).ToArray();
            var ids = nutrientIds.Distinct().ToList();
            var foods = new Dictionary<long, Food>();
            var aliasRows = new List<Alias>();
            var nutrientRows = new List<(long IngredientId, long NutrientId, decimal Amount)>();
            var statementRows = new List<Statement>();
            foreach (var scope in scopes)
            {
                foreach (var i in await db.Ingredients.IgnoreQueryFilters().AsNoTracking()
                             .Where(i => scope == null || scope.Contains(i.Id))
                             .Select(i => new { i.Id, i.Name, i.IsDeleted, i.FdcCategoryId, i.FdcDataType, i.FdcCategory, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams, i.CurationStatusId, i.FdcId })
                             .ToListAsync(cancellationToken))
                    foods[i.Id] = new Food(i.Id, i.Name ?? string.Empty, !i.IsDeleted, i.FdcCategoryId, i.FdcDataType, i.FdcCategory, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams, i.CurationStatusId == (long)CurationStatusEnum.Curated, i.FdcId);
                aliasRows.AddRange((await db.IngredientAliases.AsNoTracking()
                        .Where(a => scope == null || scope.Contains(a.IngredientId))
                        .Select(a => new { a.IngredientId, a.AliasName, a.SourceContext })
                        .ToListAsync(cancellationToken))
                    .Select(a => new Alias(a.IngredientId, a.AliasName ?? string.Empty, a.SourceContext)));
                if (ids.Count > 0)
                    nutrientRows.AddRange((await db.IngredientNutrients.AsNoTracking()
                            .Where(n => ids.Contains(n.NutrientId) && (scope == null || scope.Contains(n.IngredientId)))
                            .Select(n => new { n.IngredientId, n.NutrientId, n.Amount })
                            .ToListAsync(cancellationToken))
                        .Select(n => (n.IngredientId, n.NutrientId, n.Amount)));
                if (withStatements)
                    statementRows.AddRange((await db.IngredientContents.AsNoTracking()
                        .Where(c => c.HouseholdId == null && (scope == null || scope.Contains(c.IngredientId)))
                        .Select(c => new { c.Id, c.IngredientId, c.Outcome, c.ReaderVersion, c.LastModifiedDate })
                        .ToListAsync(cancellationToken))
                        .Select(c => new Statement(c.Id, c.IngredientId, c.Outcome, c.ReaderVersion, c.LastModifiedDate)));
            }
            var aliases = aliasRows.GroupBy(a => a.IngredientId).ToDictionary(g => g.Key, g => g.ToArray());
            var nutrients = nutrientRows.GroupBy(n => n.IngredientId)
                .ToDictionary(g => g.Key, g => g.GroupBy(n => n.NutrientId).ToDictionary(n => n.Key, n => n.Max(x => x.Amount)));
            var statements = statementRows.GroupBy(c => c.IngredientId).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).First());
            return new DietFoodCatalog(foods, aliases, nutrients, statements);
        }

        /// <summary>A catalog of the given foods as they are (or as a move would make them), with no nutrient and no statement rows beyond those given.</summary>
        public static DietFoodCatalog Of(IEnumerable<Food> foods, IEnumerable<Alias> aliases, IReadOnlyDictionary<long, Dictionary<long, decimal>>? nutrients = null, IReadOnlyDictionary<long, Statement>? statements = null, IReadOnlyList<ConsensusRow>? consensus = null) =>
            new(foods.ToDictionary(f => f.Id), aliases.GroupBy(a => a.IngredientId).ToDictionary(g => g.Key, g => g.ToArray()),
                nutrients?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<long, Dictionary<long, decimal>>(),
                statements?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<long, Statement>()) { Consensus = consensus ?? BrandedConsensus.All };

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
                .Add(food.GramsPerMilliliter).Add(food.GramsPerItem).Add(food.ReferenceServingGrams).Add(food.Curated ? "curated" : "-");
            if (Aliases.TryGetValue(foodId, out var aliases))
                foreach (var a in aliases.OrderBy(a => a.Text, StringComparer.Ordinal).ThenBy(a => a.Context, StringComparer.Ordinal)) hash.Add(a.Text).Add(a.Context);
            hash.Add("|");
            if (Nutrients.TryGetValue(foodId, out var nutrients))
                foreach (var (id, amount) in nutrients.OrderBy(n => n.Key)) hash.Add(id).Add(amount);
            hash.Add("|");
            if (Statements.TryGetValue(foodId, out var s)) hash.Add(s.Id).Add(s.Outcome).Add(s.ReaderVersion).Add(s.Modified?.Ticks);
            hash.Add("|");
            foreach (var row in ConsensusOf(food).OrderBy(r => r.Class, StringComparer.Ordinal)) hash.Add(row.Class).Add(row.Verdict);
            hash.Add("|").Add(food.FdcId?.Trim());
            foreach (var c in UnreadClassesOf(food).OrderBy(c => c, StringComparer.Ordinal)) hash.Add(c);
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
    /// (<see cref="DietPatternRule.IsMatch"/>; a branded food's name without the brand owner it closes with, and a name
    /// that is the food's own USDA description in its orders of speech, <see cref="DietPatternRule.IsDescriptionMatch"/>,
    /// staging checkpoint 6, R6 and R7), any food by a trusted alias (<see cref="DietPatternRule.IsAliasMatch"/>,
    /// measures blanked), and a live food by its USDA category (<see cref="DietCriteriaMatcher.CategoryPatterns"/>); and a
    /// pattern counts a live food that is not a prepared meal by its latest read FDC statement
    /// (<see cref="PreparedContentsGate.RestrictedNames"/>). Each food is decided from its own data only, so a batch of
    /// changed foods is decided exactly as the whole catalog would decide them.
    /// </summary>
    public static class DietFoodReader
    {
        /// <summary>A set's decision: the foods each criterion counts (by criterion id), how each counts it, and the foods a pattern matches only through an alias that does not count.</summary>
        public sealed record Decision(Dictionary<long, HashSet<long>> Foods, Dictionary<(long Criterion, long Food), string> Sources, HashSet<long> AliasOnly)
        {
            /// <summary>
            /// The live foods the set counts for nothing whose contents a rule marked not known (an alias of
            /// <see cref="RecheckDietGuard.UnknownContentsContext"/>: a wording food the link re-check took lines back to
            /// although the take-back would lower a flag, staging checkpoint 7, S15): a recipe on one is unknown, held by a set
            /// that holds unknowns.
            /// </summary>
            public HashSet<long> Unknown { get; init; } = new();

            /// <summary>The live curated foods the set counts for nothing whose branded products are split on a class a severe criterion of the set reads (<see cref="BrandedConsensus.Split"/>): their contents for the set are unknown.</summary>
            public HashSet<long> Split { get; init; } = new();

            /// <summary>The live curated foods of a made category the set counts for nothing whose brands were never read (<see cref="BrandedConsensus.UnreadClasses"/>), on a class a severe criterion of the set reads: their contents for the set are unknown, never clear.</summary>
            public HashSet<long> Unread { get; init; } = new();
        }

        /// <summary>What a batch of foods reads for every rule, computed once per rule and kept for every set that needs it.</summary>
        public sealed class Reading
        {
            private readonly DietFoodCatalog.Food[] _live;
            private readonly string[] _liveLower;
            private readonly string[] _liveNames;
            private readonly bool[] _liveDescribed;
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
                _liveNames = _live.Select(f => DietPatternRule.WithoutMaker(f.Name, f.DataType == CatalogAdmissionRule.BrandedFood)).ToArray();
                _liveDescribed = _live.Select(f => catalog.Aliases.TryGetValue(f.Id, out var a) && a.Any(x => string.Equals(x.Context?.Trim(), IngredientLinkService.UsdaDescriptionContext, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Text.Trim(), f.Name.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
                _liveLower = _liveNames.Select(n => DietPatternRule.Fold(n).ToLowerInvariant()).ToArray();
                _liveFolds = _live.Select(f => DietLineRules.FoldsToAscii(f.Name)).ToArray();
                _aliases = FoodIds.SelectMany(id => catalog.Aliases.TryGetValue(id, out var a) ? a : Array.Empty<DietFoodCatalog.Alias>()).ToArray();
                _blanked = _aliases.Select(a => DietCriteriaMatcher.AliasText(a.Text)).ToArray();
                _blankedLower = _blanked.Select(t => DietPatternRule.Fold(t).ToLowerInvariant()).ToArray();
                _rawLower = _aliases.Select(a => DietPatternRule.Fold(a.Text).ToLowerInvariant()).ToArray();
                _aliasFolds = _aliases.Select((a, i) => DietLineRules.FoldsToAscii(a.Text) || DietLineRules.FoldsToAscii(_blanked[i])).ToArray();
            }

            public DietFoodCatalog Catalog { get; }

            public IReadOnlySet<long> FoodIds { get; }

            internal IReadOnlyList<DietFoodCatalog.Food> Live => _live;

            internal (HashSet<long> Names, List<int> Aliases) Rule(DietPatternRule rule)
            {
                var key = "like:" + rule.Pattern + "\n" + rule.TypeName;
                if (_rules.TryGetValue(key, out var hit)) return hit;
                var seed = DietLineRules.Seed(rule);
                var names = new HashSet<long>();
                for (var i = 0; i < _live.Length; i++)
                {
                    if (seed != null && !_liveFolds[i] && !_liveLower[i].Contains(seed, StringComparison.Ordinal)) continue;
                    if (_liveDescribed[i] ? rule.IsDescriptionMatch(_liveNames[i]) : rule.IsMatch(_liveNames[i])) names.Add(_live[i].Id);
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
                var key = (DietLineRules.KeyOf(criterion) ?? string.Empty) + "\u0001" + (criterion.Severity >= 4 ? "4" : "0");
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

        /// <summary>The class a dried fruit is counted for, by a severe criterion of the set that reads it (<see cref="BrandedConsensus.Reads"/>).</summary>
        public const string DriedFruitClass = "sulfite";

        /// <summary>The source of a fact a dried fruit's description gave (<see cref="IsDriedFruit"/>).</summary>
        public const string DriedFruitSource = "dried-fruit";

        /// <summary>The fruit USDA's US Fruits and Fruit Juices category describes as kept by drying: a description that says dried, dehydrated, candied or sun-dried, or names prunes or raisins, and is no juice or nectar.</summary>
        private static readonly System.Text.RegularExpressions.Regex DriedWord = new(@"(?<![\p{L}])(?:dried|dehydrated|candied|prunes?|raisins?)(?![\p{L}])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Whether a live food is a fruit its own USDA description (its name or its <c>fdc:description</c> alias) gives as dried, dehydrated, candied, as prunes or as raisins, in the Fruits and Fruit Juices category (<see cref="CatalogAdmissionRule"/> category 9): such a fruit is usually sulfited (21 CFR 182.3862; staging checkpoint 8, T2: prunes and dried figs were shown for Sulfite Sensitivity), and a prepared form of it ("Plums, dried (prunes), stewed", "Figs, dried, stewed") keeps the word, so it inherits the fact.</summary>
        public static bool IsDriedFruit(DietFoodCatalog.Food food, IEnumerable<DietFoodCatalog.Alias>? aliases)
        {
            if (!food.Live || food.CategoryId != FruitCategory) return false;
            var described = (aliases ?? Array.Empty<DietFoodCatalog.Alias>()).Where(a => string.Equals(a.Context?.Trim(), IngredientLinkService.UsdaDescriptionContext, StringComparison.OrdinalIgnoreCase)).Select(a => a.Text).Prepend(food.Name).ToList();
            return described.Any(t => DriedWord.IsMatch(t)) && !described.Any(t => Pressed.IsMatch(t));
        }

        /// <summary>A juice or a nectar is pressed from the fruit, not the dried fruit ("Prune juice, canned").</summary>
        private static readonly System.Text.RegularExpressions.Regex Pressed = new(@"(?<![\p{L}])(?:juices?|nectars?)(?![\p{L}])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex AlwaysDried = new(@"(?<![\p{L}])(?:prunes?|raisins?)(?![\p{L}])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Whether a food's name names a fruit that is dried by its name (prunes, raisins): its dried-fruit fact is knowledge of the word, so a line that says "prunes" on a food no one curated is read as the dried fruit (staging checkpoint 8, T2: 96827 "nice large California prunes").</summary>
        public static bool NamesDriedFruit(string name) => AlwaysDried.IsMatch(name);

        /// <summary>USDA's Fruits and Fruit Juices category.</summary>
        public const int FruitCategory = 9;

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

            if (criteria.FirstOrDefault(c => BrandedConsensus.Reads(c, DriedFruitClass)) is { } sulfite)
                foreach (var food in reading.Live.Where(f => IsDriedFruit(f, catalog.Aliases.GetValueOrDefault(f.Id))))
                    Count(sulfite.Id, food.Id, DriedFruitSource);

            var split = new HashSet<long>();
            var unread = new HashSet<long>();
            var severe = criteria.Where(c => c.Severity >= 4 && (c.LikePattern != null || c.WordPattern != null)).ToList();
            var patterned = criteria.Where(c => c.LikePattern != null || c.WordPattern != null).ToList();
            if (patterned.Count > 0)
                foreach (var food in reading.Live)
                {
                    var rows = catalog.ConsensusOf(food);
                    if (severe.Count > 0 && catalog.UnreadClassesOf(food).Any(c => severe.Any(x => BrandedConsensus.Reads(x, c)))) unread.Add(food.Id);
                    foreach (var row in rows)
                    {
                        if (row.Verdict == BrandedConsensus.Usually)
                        {
                            foreach (var c in patterned.Where(c => BrandedConsensus.Reads(c, row.Class, anySeverity: true))) Count(c.Id, food.Id, BrandedConsensus.FactSource);
                            continue;
                        }
                        if (severe.Any(c => BrandedConsensus.Reads(c, row.Class)) && row.Verdict == BrandedConsensus.Split) split.Add(food.Id);
                    }
                }

            foreach (var ids in result.Values) aliasOnly.ExceptWith(ids);
            var countedAny = result.Values.SelectMany(ids => ids).ToHashSet();
            var unknown = reading.Live
                .Where(f => !countedAny.Contains(f.Id) && catalog.Aliases.TryGetValue(f.Id, out var aliases) && aliases.Any(a => a.Context == RecheckDietGuard.UnknownContentsContext))
                .Select(f => f.Id)
                .ToHashSet();
            aliasOnly.ExceptWith(unknown);
            split.ExceptWith(countedAny);
            split.ExceptWith(unknown);
            aliasOnly.ExceptWith(split);
            unread.ExceptWith(countedAny);
            unread.ExceptWith(unknown);
            aliasOnly.ExceptWith(unread);
            return new Decision(result, sources, aliasOnly) { Unknown = unknown, Split = split, Unread = unread };
        }
    }
}
