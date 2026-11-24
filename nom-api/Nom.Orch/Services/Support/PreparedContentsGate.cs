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

    /// <summary>A food a household's restrictions name directly, with the highest severity naming it, the words of its name's head and the major allergen classes those words name.</summary>
    public sealed record NamedRestrictedFood(long FoodId, int Severity, IReadOnlyList<string> Head, IReadOnlySet<string> Classes)
    {
        /// <summary>The food as the gate reads it from its catalog name: the head is the name before its first comma ("Peanuts, all types, raw" → peanuts).</summary>
        public static NamedRestrictedFood Of(long foodId, int severity, string name)
        {
            var head = PreparedContentsGate.Words(name.Split(',')[0]);
            var classes = IngredientStatementReader.AllergenClasses
                .Where(c => c.Value.Any(w => PreparedContentsGate.NamesWords(head, PreparedContentsGate.Words(w))))
                .Select(c => c.Key)
                .ToHashSet(StringComparer.Ordinal);
            return new NamedRestrictedFood(foodId, severity, head, classes);
        }
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

        /// <summary>The diet gate has not yet computed the foods the household's restrictions name (<see cref="RestrictedIngredientSet.Pending"/>), so no meal is offered until it has.</summary>
        public const string Pending = "diet_gate_pending";
    }

    /// <summary>
    /// Judges a prepared meal by its contents exactly as the diet gate judges a recipe by its lines
    /// (<see cref="RestrictedIngredientSet.ReadsContents"/>): each named ingredient is a line, held when the catalog food it
    /// resolved to is in the household's restricted set (<see cref="RestrictedIngredientSet.IngredientIds"/>) or when its
    /// own text names a pattern criterion (<see cref="DietCriteriaMatcher.LineNames"/>), and, for a restriction with an
    /// alcohol criterion, when it reads as a drink (no amount is given, so the alcohol that stays cannot be counted); an
    /// allergen of the "Contains" line is read with every word of its class against every pattern, and an allergen a
    /// "may contain" or shared-facility line warns of is read the same way against the criteria of severity 4 and 5
    /// (allergies and medical restrictions). A food the restrictions name directly (<see cref="RestrictedIngredientSet.NamedFoods"/>)
    /// is held by a declared allergen, or at severity 4 and 5 by a warned one, of a major allergen class its name's head
    /// names or whose own words name that head, and by a recognised term or compound whose text names that head. A label
    /// term (<see cref="StatementLabelTerms"/>) is read by its own words and by what it is made of (<see cref="LabelTerm.Contains"/>,
    /// "enriched flour" as wheat), which holds the meal like its name would; what it may be made of when the label does
    /// not say (<see cref="LabelTerm.May"/>) makes the meal unknown. The statement that decides is the household's own confirmed label, else the
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
            await set.LoadFoodsAsync(db, foodIds.Concat(resolved));
            var undecided = await DietGateStore.UndecidedAsync(db, set, foodIds);
            var namedIds = set.NamedFoods.Keys.ToList();
            var named = namedIds.Count == 0
                ? new List<NamedRestrictedFood>()
                : (await db.Ingredients.AsNoTracking().IgnoreQueryFilters()
                        .Where(i => namedIds.Contains(i.Id))
                        .Select(i => new { i.Id, i.Name })
                        .ToListAsync())
                    .Select(i => NamedRestrictedFood.Of(i.Id, set.NamedFoods[i.Id], i.Name))
                    .ToList();
            foreach (var id in foodIds.Distinct())
                result[id] = undecided.Contains(id) && !set.IngredientIds.Contains(id)
                    ? new PreparedContentsVerdict(id, PreparedContentsHeld.Pending, Array.Empty<string>())
                    : Judge(id, byFood[id].ToList(), householdId, set, curated, named);
            return result;
        }

        /// <summary>The verdict for one food from its stored statements.</summary>
        public static PreparedContentsVerdict Judge(long foodId, IReadOnlyCollection<IngredientContentsEntity> rows, long householdId, RestrictedIngredientSet set, IReadOnlySet<long> curatedFoodIds, IReadOnlyCollection<NamedRestrictedFood>? named = null)
        {
            if (set.IngredientIds.Contains(foodId)) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Food, Array.Empty<string>());
            if (set.Pending) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Pending, Array.Empty<string>());
            var label = rows.Where(r => r.HouseholdId == householdId).OrderByDescending(r => r.Id).FirstOrDefault();
            var fdc = rows.Where(r => r.HouseholdId == null).OrderByDescending(r => r.Id).FirstOrDefault();
            var decider = label ?? fdc;
            if (decider == null) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.None, Array.Empty<string>());

            var restricted = new[] { label, fdc }
                .Where(r => r != null && r.Outcome == IngredientContentsOutcome.Read)
                .SelectMany(r => RestrictedNames(r!, set, named))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (restricted.Count > 0) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Restricted, restricted);

            if (decider.Outcome != IngredientContentsOutcome.Read)
                return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Unread, decider.Reason is { } reason ? new[] { reason } : Array.Empty<string>());

            var unknown = PreparedContentsService.Items(decider)
                .Where(i => i.Kind == ContentsKinds.Unresolved || (i.Kind == ContentsKinds.Food && !curatedFoodIds.Contains(i.FoodId!.Value)) || i.LabelTerms() == null)
                .Select(i => i.Name)
                .Concat(UncertainNames(decider, set, named))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unknown.Count > 0) return new PreparedContentsVerdict(foodId, PreparedContentsHeld.Unresolved, unknown);
            return new PreparedContentsVerdict(foodId, null, Array.Empty<string>());
        }

        /// <summary>What in one read statement the household's restrictions forbid: the named ingredients and the declared or warned allergens.</summary>
        public static IEnumerable<string> RestrictedNames(IngredientContentsEntity row, RestrictedIngredientSet set, IReadOnlyCollection<NamedRestrictedFood>? named = null)
        {
            named ??= Array.Empty<NamedRestrictedFood>();
            var patterns = set.Criteria.Where(c => c.LikePattern != null || c.WordPattern != null).ToList();
            var drinks = set.Criteria.Any(c => c.NutrientId != null && DietCriteriaMatcher.IsAlcohol(c.NutrientName));
            foreach (var item in PreparedContentsService.Items(row))
            {
                if ((item.FoodId is { } food && set.IngredientIds.Contains(food))
                    || patterns.Any(c => DietCriteriaMatcher.LineNames(c, item.Name))
                    || (drinks && AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(item.Name)))
                    || (item.FoodId == null && named.Any(f => NamesWords(Words(item.Name), f.Head))))
                {
                    yield return item.Name;
                    continue;
                }
                if (FactNamed(item.LabelTerms()?.SelectMany(t => t.Contains), patterns, named, 0) is { } fact)
                    yield return item.Name + " (" + fact + ")";
            }
            var allergens = PreparedContentsService.Allergens(row);
            foreach (var allergen in allergens.Declared)
                if (IngredientStatementReader.GateWords(allergen).Any(w => patterns.Any(c => DietCriteriaMatcher.LineNames(c, w)))
                    || named.Any(f => Warns(allergen, f)))
                    yield return "contains " + allergen.Word;
            var severe = patterns.Where(c => c.Severity >= 4).ToList();
            foreach (var allergen in allergens.Advisory)
                if (IngredientStatementReader.GateWords(allergen).Any(w => severe.Any(c => DietCriteriaMatcher.LineNames(c, w)))
                    || named.Any(f => f.Severity >= 4 && Warns(allergen, f)))
                    yield return "may contain " + allergen.Word;
        }

        /// <summary>
        /// What in one read statement the household's restrictions may forbid but the label does not settle: a label
        /// term that may be made of a restricted thing, its source not stated (<see cref="LabelTerm.May"/>: a bare
        /// "lecithin" may be soy or egg, "vegetable oil" may be peanut oil). Such a meal is unknown, never offered. A term
        /// whose parts the statement lists in its bracket is decided by those parts, each read as an ingredient of its own
        /// ("vegetable oil (soybean and/or canola oil)" names its oils: staging checkpoint 5, Q8).
        /// </summary>
        public static IEnumerable<string> UncertainNames(IngredientContentsEntity row, RestrictedIngredientSet set, IReadOnlyCollection<NamedRestrictedFood>? named = null)
        {
            if (row.Outcome != IngredientContentsOutcome.Read) yield break;
            named ??= Array.Empty<NamedRestrictedFood>();
            var patterns = set.Criteria.Where(c => c.LikePattern != null || c.WordPattern != null).ToList();
            var items = PreparedContentsService.Items(row).ToList();
            var parents = ParentPositions(items);
            for (var i = 0; i < items.Count; i++)
            {
                if (Enumerable.Range(0, items.Count).Any(c => parents[c] == i && NamesSource(items[c]))) continue;
                if (FactNamed(items[i].LabelTerms()?.SelectMany(t => t.May), patterns, named, 4) is { } fact)
                    yield return items[i].Name + " (may be " + fact + ", source not stated)";
            }
        }

        /// <summary>
        /// The position of each item's own parent in the stored order, or -1 for a top-level item: a reader lists an
        /// ingredient's parts right after it, so a part belongs to the nearest open item of its parent's name, never to
        /// another occurrence of that name elsewhere in the statement (review CK10C-2).
        /// </summary>
        private static int[] ParentPositions(IReadOnlyList<ContentsItem> items)
        {
            var positions = new int[items.Count];
            var open = new List<int>();
            for (var i = 0; i < items.Count; i++)
            {
                while (open.Count > 0 && (items[i].Parent == null || items[open[^1]].Name != items[i].Parent)) open.RemoveAt(open.Count - 1);
                positions[i] = open.Count > 0 ? open[^1] : -1;
                open.Add(i);
            }
            return positions;
        }

        /// <summary>
        /// Whether a part in a bracket names a source: a food of the label table that is not itself of an unstated source
        /// ("canola oil", "soybean"). An additive, a vocabulary term or a word the reader cannot place ("TBHQ", "preserved
        /// with TBHQ", "non-hydrogenated") names none, so a bracket of only those leaves the term's source unstated (review CK10C-1).
        /// </summary>
        private static bool NamesSource(ContentsItem part)
        {
            if (part.Kind == ContentsKinds.Food) return true;
            if (part.Kind != ContentsKinds.Label) return false;
            var terms = part.LabelTerms();
            return terms is { Count: > 0 } && terms.All(t => t.Kind == LabelTermKinds.Food && t.May.Count == 0);
        }

        /// <summary>The first fact word a pattern names (read with every word of its major-allergen class), or a food the household avoids directly by its head or class, at <paramref name="minimumNamedSeverity"/> or above; null when none.</summary>
        private static string? FactNamed(IEnumerable<string>? facts, IReadOnlyCollection<DietDataCriterion> patterns, IReadOnlyCollection<NamedRestrictedFood> named, int minimumNamedSeverity)
        {
            if (facts == null) return null;
            foreach (var fact in facts)
            {
                var allergen = new StatementAllergen(fact, IngredientStatementReader.ClassOf(fact));
                if (IngredientStatementReader.GateWords(allergen).Any(w => patterns.Any(c => DietCriteriaMatcher.LineNames(c, w)))
                    || named.Any(f => f.Severity >= minimumNamedSeverity && Warns(allergen, f)))
                    return fact;
            }
            return null;
        }

        private static bool Warns(StatementAllergen allergen, NamedRestrictedFood food) =>
            (allergen.Class != null && food.Classes.Contains(allergen.Class)) || NamesWords(Words(allergen.Word), food.Head);

        /// <summary>The lower-case words of a text, each without a plural "s" or "es".</summary>
        public static IReadOnlyList<string> Words(string text) =>
            System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), @"[^\p{L}]+")
                .Where(w => w.Length > 0)
                .Select(Singular)
                .ToList();

        /// <summary>Whether <paramref name="words"/> holds <paramref name="phrase"/> as consecutive words.</summary>
        public static bool NamesWords(IReadOnlyList<string> words, IReadOnlyList<string> phrase)
        {
            if (phrase.Count == 0 || phrase.Count > words.Count) return false;
            for (var start = 0; start + phrase.Count <= words.Count; start++)
                if (phrase.Select((w, i) => words[start + i] == w).All(m => m)) return true;
            return false;
        }

        private static string Singular(string word) =>
            word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
            : word.Length > 3 && word.EndsWith("oes", StringComparison.Ordinal) ? word[..^2]
            : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1]
            : word;
    }
}
