using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// One restriction criterion the diet gate reads from data: a nutrient criterion, a word pattern
    /// (<c>\mrum\M</c>) matched on word boundaries, or a name pattern with <c>%</c> (<c>%pork%</c>)
    /// matched as ILIKE.
    /// </summary>
    public sealed record DietDataCriterion(long Id, long RestrictionTypeId, string? WordPattern, long? NutrientId, string? NutrientName, decimal? MaxAmountPerServing, int Severity, string? Notes, string? LikePattern = null);

    /// <summary>
    /// The diet gate by data (alcohol design A9). A nutrient criterion with no per-serving cap
    /// matches every food that stores the nutrient above <see cref="PresencePer100g"/> per 100 g. For
    /// alcohol, a flavouring (a food whose name reads as an extract or flavouring, or a food in USDA
    /// Spices and Herbs) does not count, as owner answer 6 leaves extracts to a note; and a food
    /// that stores no alcohol figure (an unlinked wording food) counts when its own name reads as a
    /// drink (<see cref="AlcoholLineContext.CountsForDiet"/>), never by an alias: whole recipe lines
    /// are stored as aliases ("little sugar and brandy or raisin wine" on Sugar), so an alias that
    /// holds an alcohol word decides nothing for its food. A word pattern (a Postgres pattern that
    /// starts with <c>\m</c>, matched with <c>~*</c>) matches a food whose name holds the word on word
    /// boundaries, or an alias that holds it with no alcohol word, so "rum" no longer matches Bread
    /// Crumbs nor "gin" Ginger; a matched name that reads as an alcohol word that is not a drink (a
    /// vinegar, an extract, a soft drink, a cooking liquid) does not count, and when the same
    /// restriction has a nutrient criterion, a food that stores that nutrient is left to it. An
    /// alcohol criterion with a per-serving cap (owner answer 6: No Alcohol, 0.5 g; Halal is uncapped) forbids
    /// no food that stores an alcohol figure; it excludes a recipe instead when the alcohol of its
    /// drinks that stays after cooking is above the cap per serving, or when a drink line cannot be
    /// counted (<see cref="RetainedRecipesAsync"/>, read from <see cref="RecipeAlcohol"/>), while a food
    /// with no alcohol figure whose own name reads as a drink is still forbidden, and flavourings are
    /// left to the recipe page's note. A recipe line's own text decides for the line:
    /// every live line that reads as a drink counts whatever food it is linked to
    /// (<see cref="DrinkLineRecipesAsync"/>), so a drink line still linked to a vinegar, a
    /// non-alcoholic drink or Sugar keeps its recipe flagged. Name patterns with <c>%</c> keep their
    /// ILIKE meaning and follow the same rule as the alcohol words (staging checkpoint 1, K9): a food
    /// counts by its own name, by its USDA category (<see cref="CategoryPatterns"/>: Pork Products
    /// for <c>%pork%</c>) or by an alias a rule or a person gave it (<see cref="IngredientAliasTrust"/>:
    /// its USDA description, an admin's alias, a deterministic rule's, a retired duplicate's name),
    /// never by an alias with no such source, which is a wording once seen on a recipe line ("bacon
    /// or butter" on Butter); a word pattern's alias needs such a source too. A recipe with a line on
    /// a food a pattern of a type matches only through an alias that does not count is read line by
    /// line: it stays flagged when any of its live lines, on any food, names a pattern of the type by
    /// its own text (<see cref="LineNames"/>, <see cref="TypeProbe.AliasLineRecipes"/>), so no recipe
    /// the alias flagged loses its flag while a line of it names the restricted thing, and the
    /// food's other recipes are cleared. The whole-catalog probes a household set needs are read
    /// through <see cref="DietGateCache"/> when one is given.
    /// </summary>
    public static class DietCriteriaMatcher
    {
        public const decimal PresencePer100g = 0.5m;
        public const string WordMarker = @"\m";
        public const string AlcoholNutrientName = "Alcohol";

        /// <summary>
        /// The USDA categories whose every food a name pattern counts, beside the names: a food's
        /// category is its own stored value, so a pork cut whose catalog name does not say pork
        /// still counts for <c>%pork%</c>.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, int[]> CategoryPatterns = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["%pork%"] = new[] { 10 },
        };

        /// <summary>
        /// What one restriction type's criteria match: the foods each criterion counts (by criterion
        /// id), the foods a pattern of the type matches only through an alias that does not count,
        /// and, over the whole catalog, the recipes with a live line on one of those foods that have a
        /// live line, on any food, whose own text names a pattern of the type.
        /// </summary>
        public sealed record TypeProbe(Dictionary<long, HashSet<long>> Foods, HashSet<long> AliasOnly, HashSet<long> AliasLineRecipes);

        /// <summary>Whether a criterion pattern is a word pattern rather than an ILIKE pattern.</summary>
        public static bool IsWordPattern(string? pattern) => pattern != null && pattern.TrimStart().StartsWith(WordMarker, StringComparison.Ordinal);

        /// <summary>Whether a nutrient criterion is the alcohol one.</summary>
        public static bool IsAlcohol(string? nutrientName) => string.Equals(nutrientName?.Trim(), AlcoholNutrientName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The word pattern as a .NET expression, as Postgres reads it: <c>\m</c> and <c>\M</c> (a word's start and end) become letter-or-digit boundaries, matched without case.</summary>
        public static Regex WordRegex(string wordPattern) =>
            new(wordPattern.Trim().Replace(@"\m", @"(?<![\p{L}\p{N}_])").Replace(@"\M", @"(?![\p{L}\p{N}_])"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>An ILIKE pattern as a .NET expression: <c>%</c> is any run of characters and <c>_</c> one character, the whole text matched without case.</summary>
        public static Regex LikeRegex(string likePattern) =>
            Compiled.GetOrAdd("like:" + likePattern.Trim(), _ => new Regex("^" + Regex.Escape(likePattern.Trim()).Replace("%", ".*").Replace("_", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));

        private static readonly ConcurrentDictionary<string, Regex> Compiled = new(StringComparer.Ordinal);

        /// <summary>Whether a recipe line's own text names what a pattern criterion restricts: an ILIKE pattern that matches it, or a word pattern whose match counts (<see cref="NameCounts"/>).</summary>
        public static bool LineNames(DietDataCriterion criterion, string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return false;
            if (criterion.LikePattern != null) return LikeRegex(criterion.LikePattern).IsMatch(rawLine);
            return criterion.WordPattern != null && Compiled.GetOrAdd("word:" + criterion.WordPattern, _ => WordRegex(criterion.WordPattern)).IsMatch(rawLine) && NameCounts(rawLine);
        }

        /// <summary>Whether an alias a pattern matched may count for its food by its source: only one a rule or a person gave it (<see cref="IngredientAliasTrust.IsTrusted"/>); an alias with no such source is a wording seen on a recipe line, and that line decides for itself.</summary>
        public static bool AliasSourceCounts(string? sourceContext) => IngredientAliasTrust.IsTrusted(sourceContext);

        /// <summary>Whether a name a word pattern matched counts: a name with no alcohol word counts as it is, a name with one only when it reads as a drink or a dish named after one.</summary>
        public static bool NameCounts(string name)
        {
            var reading = AlcoholLineContext.Read(name);
            return reading.Kind == AlcoholSenseKind.None || AlcoholLineContext.CountsForDiet(reading);
        }

        /// <summary>Whether an alias a word pattern matched counts for its food: only when it holds no alcohol word, since an alias with one is a recipe line or a drink's name that the food's own name and stored alcohol decide.</summary>
        public static bool AliasCounts(string alias) => AlcoholLineContext.Read(alias).Kind == AlcoholSenseKind.None;

        private static bool WordAliasCounts(NamedFood alias) => AliasSourceCounts(alias.Context) && AliasCounts(alias.Text);

        /// <summary>The nutrient, word and name-pattern criteria of the restriction types, as the gate reads them.</summary>
        public static async Task<List<DietDataCriterion>> CriteriaAsync(ApplicationDbContext db, IReadOnlyCollection<long> typeIds, int minimumSeverity = 0)
        {
            if (typeIds.Count == 0) return new List<DietDataCriterion>();
            var rows = await db.Set<RestrictionCriterionEntity>()
                .AsNoTracking()
                .Where(c => typeIds.Contains(c.RestrictionTypeId) && c.Severity >= minimumSeverity && (c.NutrientId != null || c.IngredientPattern != null))
                .Select(c => new { c.Id, c.RestrictionTypeId, c.IngredientPattern, c.NutrientId, NutrientName = c.Nutrient != null ? c.Nutrient.Name : null, c.MaxAmountPerServing, c.Severity, c.Notes })
                .ToListAsync();
            return rows
                .Where(c => c.NutrientId != null || !string.IsNullOrWhiteSpace(c.IngredientPattern))
                .Select(c => new DietDataCriterion(c.Id, c.RestrictionTypeId,
                    c.NutrientId == null && IsWordPattern(c.IngredientPattern) ? c.IngredientPattern!.Trim() : null,
                    c.NutrientId, c.NutrientName, c.MaxAmountPerServing, c.Severity, c.Notes,
                    c.NutrientId == null && !IsWordPattern(c.IngredientPattern) ? c.IngredientPattern!.Trim() : null))
                .ToList();
        }

        /// <summary>
        /// The foods each criterion matches, criteria of one restriction type at a time (a type's
        /// nutrient criteria decide its word patterns on foods that store the nutrient; its word
        /// patterns are probed together, one name and one alias query; each name pattern by name,
        /// alias and category). With a scope, only those foods are read; without one, a given cache
        /// keeps each type's answer until the catalog changes.
        /// </summary>
        public static async Task<Dictionary<DietDataCriterion, HashSet<long>>> FoodsAsync(ApplicationDbContext db, IEnumerable<DietDataCriterion> criteria, IReadOnlyCollection<long>? scope = null, DietGateCache? cache = null)
        {
            var list = criteria.ToList();
            var probes = await ProbeAsync(db, list, scope, cache);
            var result = new Dictionary<DietDataCriterion, HashSet<long>>();
            foreach (var c in list)
                if (probes.TryGetValue(c.RestrictionTypeId, out var probe) && probe.Foods.TryGetValue(c.Id, out var ids)) result[c] = new HashSet<long>(ids);
            return result;
        }

        /// <summary>
        /// Each restriction type's <see cref="TypeProbe"/>, by type id. With a scope only those foods
        /// are read and no line is read (the caller has the lines); without one the recipes of the
        /// lines on foods matched only through an alias are read too, and a given cache keeps each
        /// type's probe until the catalog changes.
        /// </summary>
        public static async Task<Dictionary<long, TypeProbe>> ProbeAsync(ApplicationDbContext db, IEnumerable<DietDataCriterion> criteria, IReadOnlyCollection<long>? scope = null, DietGateCache? cache = null)
        {
            var result = new Dictionary<long, TypeProbe>();
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId))
            {
                var list = type.ToList();
                result[type.Key] = scope == null && cache != null
                    ? await cache.GetAsync(db, "foods:" + CacheKey(list), d => TypeFoodsAsync(d, list, null))
                    : await TypeFoodsAsync(db, list, scope);
            }
            return result;
        }

        /// <summary>Whether the criteria hold the alcohol nutrient criterion with no per-serving cap, the one a recipe line's own text is read for.</summary>
        public static bool ReadsLines(IEnumerable<DietDataCriterion> criteria) => criteria.Any(c => c.NutrientId != null && c.MaxAmountPerServing == null && IsAlcohol(c.NutrientName));

        /// <summary>The alcohol nutrient criterion with a per-serving cap (the lowest cap when there are several), unless the same criteria also forbid alcohol outright; null when there is none.</summary>
        public static DietDataCriterion? RetainedCriterion(IEnumerable<DietDataCriterion> criteria)
        {
            var list = criteria.ToList();
            if (ReadsLines(list)) return null;
            return list.Where(c => c.NutrientId != null && c.MaxAmountPerServing != null && IsAlcohol(c.NutrientName)).OrderBy(c => c.MaxAmountPerServing).FirstOrDefault();
        }

        /// <summary>
        /// The recipes a type with a capped alcohol criterion (<see cref="RetainedCriterion"/>) excludes:
        /// a drink line whose amount cannot be counted, or more retained drink alcohol per serving than
        /// the cap. The whole-catalog count is kept in the cache when one is given.
        /// </summary>
        public static async Task<HashSet<long>> RetainedRecipesAsync(ApplicationDbContext db, IReadOnlyCollection<DietDataCriterion> criteria, DietGateCache? cache = null)
        {
            var result = new HashSet<long>();
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId))
            {
                if (RetainedCriterion(type) is not { } capped) continue;
                var nutrientId = capped.NutrientId!.Value;
                var alcohol = cache != null
                    ? await cache.GetAsync(db, RetainedKey(nutrientId), d => RecipeAlcoholAsync(d, nutrientId, null))
                    : await RecipeAlcoholAsync(db, nutrientId, null);
                result.UnionWith(alcohol.Where(a => a.Value.Excludes(capped.MaxAmountPerServing!.Value)).Select(a => a.Key));
            }
            return result;
        }

        private static string RetainedKey(long nutrientId) => "recipe-alcohol:" + nutrientId.ToString(CultureInfo.InvariantCulture);

        internal sealed record AlcoholLineRow(long RecipeId, long IngredientId, string? RawLine, string? LineKind, decimal Quantity, long MeasurementId, decimal? Retention, string? RetentionBasis);

        /// <summary>The live lines on the given alcoholic foods (within <paramref name="scope"/> when given), one query on the line table with no join.</summary>
        internal static IQueryable<AlcoholLineRow> AlcoholLineRows(ApplicationDbContext db, IReadOnlyCollection<long> foodIds, IReadOnlyCollection<long>? scope)
        {
            var rows = db.RecipeIngredients.AsNoTracking().Where(ri => !ri.IsDeleted && foodIds.Contains(ri.IngredientId));
            if (scope != null) rows = rows.Where(ri => scope.Contains(ri.RecipeId));
            return rows.Select(ri => new AlcoholLineRow(ri.RecipeId, ri.IngredientId, ri.RawLine, ri.LineKind, ri.Quantity, ri.MeasurementId, ri.AlcoholRetention, ri.AlcoholRetentionBasis));
        }

        /// <summary>
        /// The alcohol of every recipe with a drink (or of the recipes in <paramref name="scope"/>): the
        /// lines on foods that store the alcohol nutrient above <see cref="PresencePer100g"/> per 100 g,
        /// weighed as the label weighs them, with their stored retention, and the lines whose own text
        /// reads as a drink on any other food, counted by <see cref="RecipeAlcohol.Count"/> per the
        /// label's servings.
        /// </summary>
        public static async Task<Dictionary<long, RecipeAlcohol>> RecipeAlcoholAsync(ApplicationDbContext db, long nutrientId, IReadOnlyCollection<long>? scope)
        {
            var foods = (await db.IngredientNutrients.AsNoTracking()
                    .Where(n => n.NutrientId == nutrientId && !n.IsDeleted && n.Amount > PresencePer100g)
                    .Select(n => new { n.IngredientId, n.Amount, n.Ingredient.Name, n.Ingredient.FdcCategoryId })
                    .ToListAsync())
                .GroupBy(n => n.IngredientId)
                .ToDictionary(g => g.Key, g => (Amount: g.Max(n => n.Amount), Flavouring: RecipeAlcohol.IsFlavouringFood(g.First().Name, g.First().FdcCategoryId)));
            var foodIds = foods.Keys.ToList();

            var onFoods = foodIds.Count == 0 ? new List<AlcoholLineRow>() : await AlcoholLineRows(db, foodIds, scope).ToListAsync();
            var measurementIds = onFoods.Select(l => l.MeasurementId).Distinct().ToList();
            var measurements = measurementIds.Count == 0 ? new Dictionary<long, (long Category, decimal? Factor, bool Container)>() : (await db.Measurements.AsNoTracking()
                    .Where(m => measurementIds.Contains(m.Id))
                    .Select(m => new { m.Id, m.MeasurementCategoryId, m.BaseUnitConversionFactor, m.IsContainer })
                    .ToListAsync())
                .ToDictionary(m => m.Id, m => (Category: m.MeasurementCategoryId, Factor: m.BaseUnitConversionFactor, Container: m.IsContainer));
            var weights = foodIds.Count == 0 ? new Dictionary<long, (string Name, decimal? PerMl, decimal? PerItem, decimal? Serving)>() : (await db.Ingredients.AsNoTracking()
                    .Where(i => foodIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.Name, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams })
                    .ToListAsync())
                .ToDictionary(i => i.Id, i => (Name: i.Name, PerMl: i.GramsPerMilliliter, PerItem: i.GramsPerItem, Serving: i.ReferenceServingGrams));

            List<RecipeLine> textLines;
            if (scope == null)
            {
                textLines = await DrinkLineTextsAsync(db);
            }
            else
            {
                textLines = (await db.RecipeIngredients.AsNoTracking()
                        .Where(ri => !ri.IsDeleted && scope.Contains(ri.RecipeId))
                        .Select(ri => new RecipeLine(ri.RecipeId, ri.IngredientId, ri.RawLine))
                        .ToListAsync())
                    .Where(l => LineCounts(l.RawLine))
                    .ToList();
            }
            var other = textLines.Where(l => !foods.ContainsKey(l.IngredientId)).ToList();

            var recipeIds = onFoods.Select(l => l.RecipeId).Concat(other.Select(l => l.RecipeId)).Distinct().ToList();
            var servings = recipeIds.Count == 0 ? new Dictionary<long, decimal?>() : (await db.Recipes.AsNoTracking()
                    .Where(r => recipeIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.Servings, r.RecipeServings })
                    .ToListAsync())
                .ToDictionary(r => r.Id, r => r.Servings is > 0 ? (decimal?)r.Servings.Value : r.RecipeServings is > 0 ? r.RecipeServings : null);

            decimal? Grams(AlcoholLineRow l)
            {
                if (!measurements.TryGetValue(l.MeasurementId, out var m) || !weights.TryGetValue(l.IngredientId, out var w)) return null;
                return RecipeNutritionService.GramsFor(l.Quantity, m.Category, m.Factor, m.Container, w.PerMl, w.PerItem, w.Serving);
            }
            var facts = onFoods.Select(l => (l.RecipeId, Facts: new AlcoholLineFacts(l.IngredientId, l.RawLine, weights.TryGetValue(l.IngredientId, out var w) ? w.Name : null, l.LineKind, l.Quantity,
                    Grams(l), foods[l.IngredientId].Amount, foods[l.IngredientId].Flavouring, l.Retention, l.RetentionBasis)))
                .Concat(other.Select(l => (l.RecipeId, Facts: new AlcoholLineFacts(l.IngredientId, l.RawLine, null, null, 0m, null, null, false, null, null))));
            return facts.GroupBy(f => f.RecipeId)
                .ToDictionary(g => g.Key, g => RecipeAlcohol.Count(g.Select(f => f.Facts), servings.GetValueOrDefault(g.Key)));
        }

        /// <summary>Whether a recipe line's own text counts as alcohol for the diet gate, whatever food it is linked to.</summary>
        public static bool LineCounts(string? rawLine) => !string.IsNullOrWhiteSpace(rawLine) && AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(rawLine));

        /// <summary>
        /// The recipes with a line whose own text reads as a drink while its food is none of the
        /// <paramref name="flagged"/> foods: "1 glass of sherry" linked to Red Wine Vinegar, "sugar and
        /// brandy" linked to Sugar. Every live line is read: the lines whose text holds a drink word
        /// (<see cref="AlcoholLineContext.DrinkNamePattern"/>) are found in one probe and read by the
        /// rules, kept in the cache when one is given.
        /// </summary>
        public static async Task<HashSet<long>> DrinkLineRecipesAsync(ApplicationDbContext db, IReadOnlyCollection<DietDataCriterion> criteria, IReadOnlySet<long> flagged, DietGateCache? cache = null)
        {
            if (!ReadsLines(criteria)) return new HashSet<long>();
            var lines = cache != null ? await cache.GetAsync(db, "drink-lines", DrinkLinesAsync) : await DrinkLinesAsync(db);
            return lines.Where(l => !flagged.Contains(l.IngredientId)).Select(l => l.RecipeId).ToHashSet();
        }

        /// <summary>Computes into the cache the whole-catalog probes of every restriction type with a word or nutrient criterion or named by a restriction, as a household set reads them, so the first plan after a start does not wait for them; returns how many probes were computed.</summary>
        public static async Task<int> WarmAsync(ApplicationDbContext db, DietGateCache cache)
        {
            var before = cache.Computations;
            var inUse = await db.Restrictions.AsNoTracking()
                .Where(r => r.RestrictionTypeId != null)
                .Select(r => r.RestrictionTypeId!.Value)
                .Distinct()
                .ToListAsync();
            var rows = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Where(c => c.NutrientId != null || c.IngredientPattern != null)
                .Select(c => new { c.RestrictionTypeId, c.NutrientId, c.IngredientPattern })
                .ToListAsync();
            var typeIds = rows
                .Where(c => inUse.Contains(c.RestrictionTypeId) || c.NutrientId != null || IsWordPattern(c.IngredientPattern))
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToList();
            var criteria = await CriteriaAsync(db, typeIds);
            await FoodsAsync(db, criteria, cache: cache);
            if (ReadsLines(criteria)) await cache.GetAsync(db, "drink-lines", DrinkLinesAsync);
            foreach (var nutrientId in criteria.GroupBy(c => c.RestrictionTypeId).Select(t => RetainedCriterion(t)?.NutrientId).OfType<long>().Distinct())
                await cache.GetAsync(db, RetainedKey(nutrientId), d => RecipeAlcoholAsync(d, nutrientId, null));
            return cache.Computations - before;
        }

        /// <summary>One live recipe line whose own text counts as a drink, and the food it is linked to.</summary>
        public sealed record DrinkLine(long RecipeId, long IngredientId);

        internal sealed record RecipeLine(long RecipeId, long IngredientId, string RawLine);

        /// <summary>Every live line whose text holds a drink word (<c>~*</c>), one probe with no list of foods, read by the rules after.</summary>
        internal static IQueryable<RecipeLine> DrinkLineRows(ApplicationDbContext db) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && Regex.IsMatch(ri.RawLine, AlcoholLineContext.DrinkNamePattern, RegexOptions.IgnoreCase))
                .Select(ri => new RecipeLine(ri.RecipeId, ri.IngredientId, ri.RawLine));

        private sealed record AliasRow(string AliasName, string? SourceContext);

        internal sealed record NutrientFood(long IngredientId, string Name, int? CategoryId);

        internal sealed record NamedFood(long Id, string Text, bool IsAlias = false, string? Context = null);

        internal static IQueryable<NutrientFood> NutrientRows(ApplicationDbContext db, long nutrientId, IReadOnlyCollection<long>? scope)
        {
            var rows = db.IngredientNutrients.AsNoTracking().Where(n => n.NutrientId == nutrientId && !n.IsDeleted && n.Amount > PresencePer100g);
            if (scope != null) rows = rows.Where(n => scope.Contains(n.IngredientId));
            return rows.Select(n => new NutrientFood(n.IngredientId, n.Ingredient.Name, n.Ingredient.FdcCategoryId));
        }

        /// <summary>Foods whose name holds the word pattern (<c>~*</c>, which the trigram index on Ingredient.Name serves): a name probe on its own.</summary>
        internal static IQueryable<NamedFood> WordNameRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            var foods = db.Ingredients.AsNoTracking().Where(i => Regex.IsMatch(i.Name, pattern, RegexOptions.IgnoreCase));
            if (scope != null) foods = foods.Where(i => scope.Contains(i.Id));
            return foods.Select(i => new NamedFood(i.Id, i.Name, false));
        }

        /// <summary>Live aliases that hold the word pattern, probed apart from the names (an OR of the two forces a scan).</summary>
        internal static IQueryable<NamedFood> WordAliasRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            var aliases = db.IngredientAliases.AsNoTracking().Where(a => !a.IsDeleted && Regex.IsMatch(a.AliasName, pattern, RegexOptions.IgnoreCase));
            if (scope != null) aliases = aliases.Where(a => scope.Contains(a.IngredientId));
            return aliases.Select(a => new NamedFood(a.IngredientId, a.AliasName, true, a.SourceContext));
        }

        /// <summary>Foods whose name an ILIKE pattern matches, probed apart from the aliases (the trigram index on Ingredient.Name serves it).</summary>
        internal static IQueryable<NamedFood> LikeNameRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            var foods = db.Ingredients.AsNoTracking().Where(i => EF.Functions.ILike(i.Name, pattern));
            if (scope != null) foods = foods.Where(i => scope.Contains(i.Id));
            return foods.Select(i => new NamedFood(i.Id, i.Name, false, null));
        }

        /// <summary>Live aliases an ILIKE pattern matches, with the source each was given by.</summary>
        internal static IQueryable<NamedFood> LikeAliasRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            var aliases = db.IngredientAliases.AsNoTracking().Where(a => !a.IsDeleted && EF.Functions.ILike(a.AliasName, pattern));
            if (scope != null) aliases = aliases.Where(a => scope.Contains(a.IngredientId));
            return aliases.Select(a => new NamedFood(a.IngredientId, a.AliasName, true, a.SourceContext));
        }

        /// <summary>The live lines whose text an ILIKE pattern matches, of the recipes with a live line on one of the given foods.</summary>
        internal static IQueryable<RecipeLine> LikeLineRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long> foods) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && EF.Functions.ILike(ri.RawLine, pattern)
                    && db.RecipeIngredients.Any(o => !o.IsDeleted && o.RecipeId == ri.RecipeId && foods.Contains(o.IngredientId)))
                .Select(ri => new RecipeLine(ri.RecipeId, ri.IngredientId, ri.RawLine));

        /// <summary>The live lines whose text a word pattern matches (<c>~*</c>), of the recipes with a live line on one of the given foods.</summary>
        internal static IQueryable<RecipeLine> WordLineRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long> foods) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && Regex.IsMatch(ri.RawLine, pattern, RegexOptions.IgnoreCase)
                    && db.RecipeIngredients.Any(o => !o.IsDeleted && o.RecipeId == ri.RecipeId && foods.Contains(o.IngredientId)))
                .Select(ri => new RecipeLine(ri.RecipeId, ri.IngredientId, ri.RawLine));

        private static string CacheKey(IEnumerable<DietDataCriterion> criteria) =>
            string.Join(';', criteria.OrderBy(c => c.Id).Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Id}:{c.WordPattern}:{c.LikePattern}:{c.NutrientId}:{c.NutrientName}:{c.MaxAmountPerServing}")));

        private static async Task<TypeProbe> TypeFoodsAsync(ApplicationDbContext db, IReadOnlyList<DietDataCriterion> criteria, IReadOnlyCollection<long>? scope)
        {
            var result = new Dictionary<long, HashSet<long>>();
            var aliasOnly = new HashSet<long>();
            var nutrients = criteria.Where(c => c.NutrientId != null && c.MaxAmountPerServing == null).ToList();
            foreach (var c in nutrients) result[c.Id] = await NutrientFoodsAsync(db, c, scope);
            if (RetainedCriterion(criteria) is { } capped) result[capped.Id] = await NoFigureDrinkFoodsAsync(db, capped.NutrientId!.Value, scope);
            var words = criteria.Where(c => c.WordPattern != null).ToList();
            if (words.Count > 0)
            {
                var all = await NamesAsync(db, string.Join('|', words.Select(c => "(?:" + c.WordPattern + ")")), scope, aliases: true);
                var named = all.Where(n => n.IsAlias ? WordAliasCounts(n) : NameCounts(n.Text)).ToList();
                var decided = (await StoringAsync(db, named.Select(n => n.Id).Distinct().ToList(), criteria.Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).Distinct().ToList())).ToHashSet();
                foreach (var c in words)
                {
                    var word = WordRegex(c.WordPattern!);
                    result[c.Id] = named.Where(n => !decided.Contains(n.Id) && word.IsMatch(n.Text)).Select(n => n.Id).ToHashSet();
                }
                aliasOnly.UnionWith(all.Where(n => n.IsAlias && !WordAliasCounts(n)).Select(n => n.Id));
            }
            foreach (var c in criteria.Where(c => c.LikePattern != null))
            {
                var (counted, onlyByAlias) = await LikeFoodsAsync(db, c.LikePattern!, scope);
                result[c.Id] = counted;
                aliasOnly.UnionWith(onlyByAlias);
            }
            foreach (var ids in result.Values) aliasOnly.ExceptWith(ids);
            var recipes = scope == null && aliasOnly.Count > 0 ? await AliasLineRecipesAsync(db, criteria, aliasOnly) : new HashSet<long>();
            return new TypeProbe(result, aliasOnly, recipes);
        }

        private static async Task<(HashSet<long> Counted, HashSet<long> AliasOnly)> LikeFoodsAsync(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            List<NamedFood> names;
            List<NamedFood> aliases;
            if (db.Database.IsRelational())
            {
                names = await LikeNameRows(db, pattern, scope).ToListAsync();
                aliases = await LikeAliasRows(db, pattern, scope).ToListAsync();
            }
            else
            {
                var like = LikeRegex(pattern);
                var foods = db.Ingredients.AsNoTracking();
                if (scope != null) foods = foods.Where(i => scope.Contains(i.Id));
                var all = await foods.Select(i => new { i.Id, i.Name, Aliases = i.Aliases.Where(a => !a.IsDeleted).Select(a => new { a.AliasName, a.SourceContext }).ToList() }).ToListAsync();
                names = all.Where(i => like.IsMatch(i.Name)).Select(i => new NamedFood(i.Id, i.Name)).ToList();
                aliases = all.SelectMany(i => i.Aliases.Where(a => like.IsMatch(a.AliasName)).Select(a => new NamedFood(i.Id, a.AliasName, true, a.SourceContext))).ToList();
            }
            var counted = names.Select(n => n.Id).Concat(aliases.Where(a => AliasSourceCounts(a.Context)).Select(a => a.Id)).ToHashSet();
            if (CategoryPatterns.TryGetValue(pattern, out var categories))
            {
                var inCategory = db.Ingredients.AsNoTracking().Where(i => i.FdcCategoryId != null && categories.Contains(i.FdcCategoryId.Value));
                if (scope != null) inCategory = inCategory.Where(i => scope.Contains(i.Id));
                counted.UnionWith(await inCategory.Select(i => i.Id).ToListAsync());
            }
            var onlyByAlias = aliases.Select(a => a.Id).Where(id => !counted.Contains(id)).ToHashSet();
            return (counted, onlyByAlias);
        }

        private static async Task<HashSet<long>> AliasLineRecipesAsync(ApplicationDbContext db, IReadOnlyList<DietDataCriterion> criteria, HashSet<long> foods)
        {
            var patterns = criteria.Where(c => c.LikePattern != null || c.WordPattern != null).ToList();
            var ids = foods.ToList();
            var lines = new List<RecipeLine>();
            if (db.Database.IsRelational())
            {
                foreach (var like in patterns.Where(c => c.LikePattern != null).Select(c => c.LikePattern!).Distinct(StringComparer.OrdinalIgnoreCase))
                    lines.AddRange(await LikeLineRows(db, like, ids).ToListAsync());
                var words = patterns.Where(c => c.WordPattern != null).Select(c => "(?:" + c.WordPattern + ")").ToList();
                if (words.Count > 0) lines.AddRange(await WordLineRows(db, string.Join('|', words), ids).ToListAsync());
            }
            else
            {
                var touched = await db.RecipeIngredients.AsNoTracking().Where(ri => !ri.IsDeleted && ids.Contains(ri.IngredientId)).Select(ri => ri.RecipeId).Distinct().ToListAsync();
                lines = await db.RecipeIngredients.AsNoTracking().Where(ri => !ri.IsDeleted && touched.Contains(ri.RecipeId))
                    .Select(ri => new RecipeLine(ri.RecipeId, ri.IngredientId, ri.RawLine)).ToListAsync();
            }
            return lines.Where(l => patterns.Any(c => LineNames(c, l.RawLine))).Select(l => l.RecipeId).ToHashSet();
        }

        private static async Task<List<DrinkLine>> DrinkLinesAsync(ApplicationDbContext db) =>
            (await DrinkLineTextsAsync(db)).Select(l => new DrinkLine(l.RecipeId, l.IngredientId)).Distinct().ToList();

        private static async Task<List<RecipeLine>> DrinkLineTextsAsync(ApplicationDbContext db)
        {
            List<RecipeLine> lines;
            if (db.Database.IsRelational())
            {
                lines = await DrinkLineRows(db).ToListAsync();
            }
            else
            {
                var word = WordRegex(AlcoholLineContext.DrinkNamePattern);
                lines = (await db.RecipeIngredients.AsNoTracking().Where(ri => !ri.IsDeleted).Select(ri => new RecipeLine(ri.RecipeId, ri.IngredientId, ri.RawLine)).ToListAsync())
                    .Where(l => l.RawLine != null && word.IsMatch(l.RawLine))
                    .ToList();
            }
            return lines.Where(l => LineCounts(l.RawLine)).ToList();
        }

        private static async Task<HashSet<long>> NutrientFoodsAsync(ApplicationDbContext db, DietDataCriterion criterion, IReadOnlyCollection<long>? scope)
        {
            var nutrientId = criterion.NutrientId!.Value;
            var rows = await NutrientRows(db, nutrientId, scope).ToListAsync();
            if (!IsAlcohol(criterion.NutrientName)) return rows.Select(r => r.IngredientId).ToHashSet();

            var ids = rows
                .Where(r => r.CategoryId != CatalogAdmissionRule.SpicesAndHerbs && !AlcoholLineContext.IsFlavouring(AlcoholLineContext.Read(r.Name)))
                .Select(r => r.IngredientId)
                .ToHashSet();
            ids.UnionWith(await NoFigureDrinkFoodsAsync(db, nutrientId, scope));
            return ids;
        }

        /// <summary>The foods that store no figure for the alcohol nutrient and whose own name reads as a drink (never by an alias).</summary>
        private static async Task<HashSet<long>> NoFigureDrinkFoodsAsync(ApplicationDbContext db, long nutrientId, IReadOnlyCollection<long>? scope)
        {
            var named = (await NamesAsync(db, AlcoholLineContext.DrinkNamePattern, scope, aliases: false))
                .Where(n => AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(n.Text)))
                .Select(n => n.Id)
                .ToHashSet();
            named.ExceptWith(await StoringAsync(db, named, new[] { nutrientId }));
            return named;
        }

        private static async Task<List<NamedFood>> NamesAsync(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope, bool aliases)
        {
            if (db.Database.IsRelational())
            {
                var names = await WordNameRows(db, pattern, scope).ToListAsync();
                if (aliases) names.AddRange(await WordAliasRows(db, pattern, scope).ToListAsync());
                return names;
            }

            var word = WordRegex(pattern);
            var foods = db.Ingredients.AsNoTracking();
            if (scope != null) foods = foods.Where(i => scope.Contains(i.Id));
            var all = await foods.Select(i => new { i.Id, i.Name, Aliases = i.Aliases.Where(a => !a.IsDeleted).Select(a => new AliasRow(a.AliasName, a.SourceContext)).ToList() }).ToListAsync();
            return all.SelectMany(i => (aliases ? i.Aliases : new()).Select(a => new NamedFood(i.Id, a.AliasName, true, a.SourceContext)).Prepend(new NamedFood(i.Id, i.Name, false)).Where(n => word.IsMatch(n.Text))).ToList();
        }

        private static async Task<List<long>> StoringAsync(ApplicationDbContext db, IReadOnlyCollection<long> ids, IReadOnlyCollection<long> nutrientIds)
        {
            if (ids.Count == 0 || nutrientIds.Count == 0) return new List<long>();
            var candidates = ids.ToList();
            return await db.IngredientNutrients.AsNoTracking()
                .Where(n => !n.IsDeleted && nutrientIds.Contains(n.NutrientId) && candidates.Contains(n.IngredientId))
                .Select(n => n.IngredientId)
                .Distinct()
                .ToListAsync();
        }
    }
}
