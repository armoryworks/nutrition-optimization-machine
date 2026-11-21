using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>Why the contents gate holds a prepared meal back from a household, or null when it offers it, with the ingredients or allergens that decided it.</summary>
    public sealed record PreparedContentsVerdict(long FoodId, string? Held, IReadOnlyList<string> Names)
    {
        public bool Offered => Held == null;
    }

    /// <summary>The reasons <see cref="PreparedContentsGate"/> gives.</summary>
    public static class PreparedContentsHeld
    {
        /// <summary>No authoritative ingredient statement is stored for the meal.</summary>
        public const string None = "contents_none";

        /// <summary>The statement that decides could not be read, so the contents are unknown.</summary>
        public const string Unread = "contents_unread";

        /// <summary>The statement was read, but a named ingredient is neither a curated catalog food nor a recognised term.</summary>
        public const string Unresolved = "contents_unresolved";

        /// <summary>The statement names something the household's restrictions forbid, or declares or warns of an allergen they forbid.</summary>
        public const string Restricted = "contents_restricted";

        /// <summary>The meal itself is a food the household's restrictions name.</summary>
        public const string Food = "restricted_food";
    }

    /// <summary>
    /// Judges a prepared meal by its contents exactly as the diet gate judges a recipe by its lines
    /// (<see cref="RestrictedIngredientSet.ReadsContents"/>): each named ingredient is a line, held when the catalog food it
    /// resolved to is in the household's restricted set (<see cref="RestrictedIngredientSet.IngredientIds"/>) or when its
    /// own text names a pattern criterion (<see cref="DietCriteriaMatcher.LineNames"/>), and, for a restriction with an
    /// alcohol criterion, when it reads as a drink (no amount is given, so the alcohol that stays cannot be counted); an
    /// allergen of the "Contains" line is read with every word of its class against every pattern, and an allergen a
    /// "may contain" or shared-facility line warns of is read the same way against the criteria of severity 4 and 5
    /// (allergies and medical restrictions). The statement that decides is the household's own confirmed label, else the
    /// FDC statement; a restricted thing named in either holds the meal. A meal is offered only when the deciding statement
    /// was read, every named ingredient resolved to a curated food (still curated) or was recognised, and nothing is
    /// restricted; anything unknown keeps it out.
    /// </summary>
    public static class PreparedContentsGate
    {
        /// <summary>The verdict for each food, from the live statements of the household and of FDC.</summary>
        public static async Task<Dictionary<long, PreparedContentsVerdict>> JudgeAsync(ApplicationDbContext db, long householdId, RestrictedIngredientSet set, IReadOnlyCollection<long> foodIds)
        {
            var result = new Dictionary<long, PreparedContentsVerdict>();
            if (foodIds.Count == 0) return result;
            var rows = await db.IngredientContents.AsNoTracking()
                .Where(c => foodIds.Contains(c.IngredientId) && (c.HouseholdId == null || c.HouseholdId == householdId))
                .ToListAsync();
            var byFood = rows.ToLookup(r => r.IngredientId);
            var resolved = rows.Where(r => r.Outcome == IngredientContentsOutcome.Read)
                .SelectMany(r => PreparedContentsService.Items(r)).Where(i => i.FoodId != null).Select(i => i.FoodId!.Value).Distinct().ToList();
            var curated = (await db.Ingredients.AsNoTracking()
                    .Where(i => resolved.Contains(i.Id) && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Where(PreparedFoods.NotPrepared)
                    .Select(i => i.Id)
                    .ToListAsync())
                .ToHashSet();
            foreach (var id in foodIds.Distinct()) result[id] = Judge(id, byFood[id].ToList(), householdId, set, curated);
            return result;
        }

        /// <summary>The verdict for one food from its stored statements.</summary>
        public static PreparedContentsVerdict Judge(long foodId, IReadOnlyCollection<IngredientContentsEntity> rows, long householdId, RestrictedIngredientSet set, IReadOnlySet<long> curatedFoodIds)
        {
            if (set.IngredientIds.Contains(foodId)) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Food, Array.Empty<string>());
            var label = rows.Where(r => r.HouseholdId == householdId).OrderByDescending(r => r.Id).FirstOrDefault();
            var fdc = rows.Where(r => r.HouseholdId == null).OrderByDescending(r => r.Id).FirstOrDefault();
            var decider = label ?? fdc;
            if (decider == null) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.None, Array.Empty<string>());

            var restricted = new[] { label, fdc }
                .Where(r => r != null && r.Outcome == IngredientContentsOutcome.Read)
                .SelectMany(r => RestrictedNames(r!, set))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (restricted.Count > 0) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Restricted, restricted);

            if (decider.Outcome != IngredientContentsOutcome.Read)
                return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Unread, decider.Reason is { } reason ? new[] { reason } : Array.Empty<string>());

            var unknown = PreparedContentsService.Items(decider)
                .Where(i => i.Kind == ContentsKinds.Unresolved || (i.Kind == ContentsKinds.Food && !curatedFoodIds.Contains(i.FoodId!.Value)))
                .Select(i => i.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unknown.Count > 0) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Unresolved, unknown);
            return new PreparedContentsVerdict(foodId, null, Array.Empty<string>());
        }

        /// <summary>What in one read statement the household's restrictions forbid: the named ingredients and the declared or warned allergens.</summary>
        public static IEnumerable<string> RestrictedNames(IngredientContentsEntity row, RestrictedIngredientSet set)
        {
            var patterns = set.Criteria.Where(c => c.LikePattern != null || c.WordPattern != null).ToList();
            var drinks = set.Criteria.Any(c => c.NutrientId != null && DietCriteriaMatcher.IsAlcohol(c.NutrientName));
            foreach (var item in PreparedContentsService.Items(row))
            {
                if ((item.FoodId is { } food && set.IngredientIds.Contains(food))
                    || patterns.Any(c => DietCriteriaMatcher.LineNames(c, item.Name))
                    || (drinks && AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(item.Name))))
                    yield return item.Name;
            }
            var allergens = PreparedContentsService.Allergens(row);
            foreach (var allergen in allergens.Declared)
                if (IngredientStatementReader.GateWords(allergen).Any(w => patterns.Any(c => DietCriteriaMatcher.LineNames(c, w))))
                    yield return "contains " + allergen.Word;
            var severe = patterns.Where(c => c.Severity >= 4).ToList();
            foreach (var allergen in allergens.Advisory)
                if (IngredientStatementReader.GateWords(allergen).Any(w => severe.Any(c => DietCriteriaMatcher.LineNames(c, w))))
                    yield return "may contain " + allergen.Word;
        }
    }
}
