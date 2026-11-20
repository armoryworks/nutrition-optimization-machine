using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// One restriction criterion the diet gate reads from data rather than by a plain name pattern:
    /// a nutrient criterion, or a word pattern (<c>\mrum\M</c>) matched on word boundaries.
    /// </summary>
    public sealed record DietDataCriterion(long Id, long RestrictionTypeId, string? WordPattern, long? NutrientId, string? NutrientName, decimal? MaxAmountPerServing, int Severity, string? Notes);

    /// <summary>
    /// The diet gate by data (alcohol design A9). A nutrient criterion with no per-serving cap
    /// matches every food that stores the nutrient above <see cref="PresencePer100g"/> per 100 g. For
    /// alcohol, a flavouring (a food whose name reads as an extract or flavouring, or a food in USDA
    /// Spices and Herbs) does not count, as owner answer 6 leaves extracts to a note; and a food
    /// that stores no alcohol figure (an unlinked wording food) counts when its name or an alias
    /// reads as a drink (<see cref="AlcoholLineContext.CountsForDiet"/>). A word pattern (a Postgres
    /// pattern that starts with <c>\m</c>, matched with <c>~*</c>) matches a food whose name or an
    /// alias holds the word on word boundaries, so "rum" no longer matches Bread Crumbs nor "gin"
    /// Ginger; a matched name that reads as an alcohol word that is not a drink (a vinegar, an
    /// extract, a soft drink, a cooking liquid) does not count, and when the same restriction has
    /// a nutrient criterion, a food that stores that nutrient is left to it. A criterion with a
    /// per-serving cap is not read here: the per-serving exclusion of retained alcohol belongs to
    /// the alcohol-retention item. Name patterns with <c>%</c> keep their ILIKE meaning and are
    /// matched where they were before.
    /// </summary>
    public static class DietCriteriaMatcher
    {
        public const decimal PresencePer100g = 0.5m;
        public const string WordMarker = @"\m";
        public const string AlcoholNutrientName = "Alcohol";

        /// <summary>Whether a criterion pattern is a word pattern rather than an ILIKE pattern.</summary>
        public static bool IsWordPattern(string? pattern) => pattern != null && pattern.TrimStart().StartsWith(WordMarker, StringComparison.Ordinal);

        /// <summary>Whether a nutrient criterion is the alcohol one.</summary>
        public static bool IsAlcohol(string? nutrientName) => string.Equals(nutrientName?.Trim(), AlcoholNutrientName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The word pattern as a .NET expression, as Postgres reads it: <c>\m</c> and <c>\M</c> (a word's start and end) become letter-or-digit boundaries, matched without case.</summary>
        public static Regex WordRegex(string wordPattern) =>
            new(wordPattern.Trim().Replace(@"\m", @"(?<![\p{L}\p{N}_])").Replace(@"\M", @"(?![\p{L}\p{N}_])"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Whether a name a word pattern matched counts: a name with no alcohol word counts as it is, a name with one only when it reads as a drink or a dish named after one.</summary>
        public static bool NameCounts(string name)
        {
            var reading = AlcoholLineContext.Read(name);
            return reading.Kind == AlcoholSenseKind.None || AlcoholLineContext.CountsForDiet(reading);
        }

        /// <summary>The word and nutrient criteria of the restriction types, as the gate reads them.</summary>
        public static async Task<List<DietDataCriterion>> CriteriaAsync(ApplicationDbContext db, IReadOnlyCollection<long> typeIds, int minimumSeverity = 0)
        {
            if (typeIds.Count == 0) return new List<DietDataCriterion>();
            var rows = await db.Set<RestrictionCriterionEntity>()
                .AsNoTracking()
                .Where(c => typeIds.Contains(c.RestrictionTypeId) && c.Severity >= minimumSeverity && (c.NutrientId != null || c.IngredientPattern != null))
                .Select(c => new { c.Id, c.RestrictionTypeId, c.IngredientPattern, c.NutrientId, NutrientName = c.Nutrient != null ? c.Nutrient.Name : null, c.MaxAmountPerServing, c.Severity, c.Notes })
                .ToListAsync();
            return rows
                .Where(c => c.NutrientId != null || IsWordPattern(c.IngredientPattern))
                .Select(c => new DietDataCriterion(c.Id, c.RestrictionTypeId, c.NutrientId == null ? c.IngredientPattern!.Trim() : null, c.NutrientId, c.NutrientName, c.MaxAmountPerServing, c.Severity, c.Notes))
                .ToList();
        }

        /// <summary>
        /// The foods each criterion matches, criteria of one restriction type at a time (a type's
        /// nutrient criteria decide its word patterns on foods that store the nutrient; its word
        /// patterns are probed together, one name and one alias query). With a scope, only those
        /// foods are read.
        /// </summary>
        public static async Task<Dictionary<DietDataCriterion, HashSet<long>>> FoodsAsync(ApplicationDbContext db, IEnumerable<DietDataCriterion> criteria, IReadOnlyCollection<long>? scope = null)
        {
            var result = new Dictionary<DietDataCriterion, HashSet<long>>();
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId))
            {
                var nutrients = type.Where(c => c.NutrientId != null && c.MaxAmountPerServing == null).ToList();
                foreach (var c in nutrients) result[c] = await NutrientFoodsAsync(db, c, scope);
                var words = type.Where(c => c.WordPattern != null).ToList();
                if (words.Count == 0) continue;
                var named = (await NamesAsync(db, string.Join('|', words.Select(c => "(?:" + c.WordPattern + ")")), scope))
                    .Where(n => NameCounts(n.Text))
                    .ToList();
                var decided = (await StoringAsync(db, named.Select(n => n.Id).Distinct().ToList(), nutrients.Select(c => c.NutrientId!.Value).Distinct().ToList())).ToHashSet();
                foreach (var c in words)
                {
                    var word = WordRegex(c.WordPattern!);
                    result[c] = named.Where(n => !decided.Contains(n.Id) && word.IsMatch(n.Text)).Select(n => n.Id).ToHashSet();
                }
            }
            return result;
        }

        internal sealed record NutrientFood(long IngredientId, string Name, int? CategoryId);

        internal sealed record NamedFood(long Id, string Text);

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
            return foods.Select(i => new NamedFood(i.Id, i.Name));
        }

        /// <summary>Live aliases that hold the word pattern, probed apart from the names (an OR of the two forces a scan).</summary>
        internal static IQueryable<NamedFood> WordAliasRows(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            var aliases = db.IngredientAliases.AsNoTracking().Where(a => !a.IsDeleted && Regex.IsMatch(a.AliasName, pattern, RegexOptions.IgnoreCase));
            if (scope != null) aliases = aliases.Where(a => scope.Contains(a.IngredientId));
            return aliases.Select(a => new NamedFood(a.IngredientId, a.AliasName));
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
            var named = (await NamesAsync(db, AlcoholLineContext.DrinkNamePattern, scope))
                .Where(n => AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(n.Text)))
                .Select(n => n.Id)
                .ToHashSet();
            named.ExceptWith(await StoringAsync(db, named, new[] { nutrientId }));
            ids.UnionWith(named);
            return ids;
        }

        private static async Task<List<NamedFood>> NamesAsync(ApplicationDbContext db, string pattern, IReadOnlyCollection<long>? scope)
        {
            if (db.Database.IsRelational())
                return (await WordNameRows(db, pattern, scope).ToListAsync()).Concat(await WordAliasRows(db, pattern, scope).ToListAsync()).ToList();

            var word = WordRegex(pattern);
            var foods = db.Ingredients.AsNoTracking();
            if (scope != null) foods = foods.Where(i => scope.Contains(i.Id));
            var all = await foods.Select(i => new { i.Id, i.Name, Aliases = i.Aliases.Where(a => !a.IsDeleted).Select(a => a.AliasName).ToList() }).ToListAsync();
            return all.SelectMany(i => i.Aliases.Prepend(i.Name).Where(t => word.IsMatch(t)).Select(t => new NamedFood(i.Id, t))).ToList();
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
