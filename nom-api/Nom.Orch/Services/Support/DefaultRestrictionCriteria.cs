using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Baseline <see cref="RestrictionCriterionEntity"/> rows for the common allergies,
    /// intolerances, diets and religious rules, keyed by restriction *name* so both the
    /// legacy (2000-series) and current (60xxx) reference sets — and any tenant — resolve.
    /// Idempotent: a restriction type whose criteria were seeded or admin-edited in
    /// Admin → Diet Categories (any non-default or deleted row) is left alone; types with
    /// none, or with only untouched defaults, get whichever default patterns they lack.
    /// Patterns are ILIKE against ingredient names and aliases, except word patterns
    /// (<c>\mrum\M</c>), which match whole words, and the alcohol diets also carry a nutrient
    /// row (<see cref="NutrientsByName"/>; both read by <see cref="DietCriteriaMatcher"/>).
    /// Severity 5 = allergy, 4 = medical/intolerance, 3 = diet/religious.
    /// </summary>
    public static class DefaultRestrictionCriteria
    {
        internal const string DefaultNote = "default (name-keyed baseline)";

        private static readonly string[] Meat = { "%beef%", "%pork%", "%lamb%", "%veal%", "%venison%", "%bison%", "%goat%", "%bacon%", "%ham%", "%sausage%", "%pepperoni%", "%salami%", "%prosciutto%", "%steak%", "%ground meat%", "%meatball%", "%hot dog%", "%chorizo%", "%pancetta%", "%lard%", "%gelatin%" };
        private static readonly string[] Poultry = { "%chicken%", "%turkey%", "%duck%", "%goose%", "%quail%", "%poultry%" };
        private static readonly string[] Fish = { "%salmon%", "%tuna%", "%cod%", "%tilapia%", "%trout%", "%halibut%", "%anchov%", "%sardine%", "%mackerel%", "%haddock%", "%bass%", "%snapper%", "%swordfish%", "%mahi%", "%fish%", "%catfish%", "%herring%", "%pollock%", "%flounder%", "%sole%" };
        private static readonly string[] Shellfish = { "%shrimp%", "%prawn%", "%crab%", "%lobster%", "%clam%", "%mussel%", "%oyster%", "%scallop%", "%crawfish%", "%crayfish%", "%squid%", "%calamari%", "%octopus%", "%shellfish%" };
        private static readonly string[] Dairy = { "%milk%", "%cheese%", "%butter%", "%yogurt%", "%yoghurt%", "%cream%", "%whey%", "%casein%", "%ghee%", "%kefir%", "%custard%", "%ice cream%", "%mozzarella%", "%cheddar%", "%parmesan%", "%feta%", "%ricotta%", "%brie%", "%gouda%" };
        private static readonly string[] Eggs = { "%egg%", "%mayonnaise%", "%meringue%", "%aioli%" };
        private static readonly string[] Honey = { "%honey%" };
        private static readonly string[] Gluten = { "%wheat%", "%flour%", "%barley%", "%rye%", "%bread%", "%pasta%", "%spaghetti%", "%noodle%", "%couscous%", "%seitan%", "%bulgur%", "%farro%", "%semolina%", "%cracker%", "%tortilla%", "%pita%", "%bagel%", "%croissant%", "%breadcrumb%", "%panko%", "%soy sauce%", "%malt%", "%beer%", "oat%", "% oat%", "%granola%", "%muesli%", "%spelt%", "%kamut%", "%triticale%", "%farina%", "%orzo%", "%gnocchi%" };
        private static readonly string[] Peanut = { "%peanut%" };
        private static readonly string[] TreeNuts = { "%almond%", "%walnut%", "%cashew%", "%pecan%", "%pistachio%", "%hazelnut%", "%macadamia%", "%brazil nut%", "%pine nut%", "%chestnut%", "%tree nut%", "%mixed nut%", "%nut butter%", "%marzipan%", "%praline%", "%nutella%" };
        private static readonly string[] Soy = { "%soy%", "%tofu%", "%tempeh%", "%edamame%", "%miso%", "%tamari%", "%natto%" };
        private static readonly string[] Sesame = { "%sesame%", "%tahini%", "%halva%", "%hummus%" };
        private static readonly string[] Corn = { "%corn%", "%maize%", "%polenta%", "%grits%", "%popcorn%", "%tortilla%" };
        private static readonly string[] Coconut = { "%coconut%" };
        private static readonly string[] Mustard = { "%mustard%" };
        private static readonly string[] Celery = { "%celery%", "%celeriac%" };
        private static readonly string[] Alcohol =
        {
            @"\mwines?\M", @"\mbeers?\M", @"\males?\M", @"\mlager\M", @"\mstout\M", @"\mvodka\M", @"\mrum\M", @"\mwhiske?y\M", @"\mbourbon\M", @"\mscotch\M",
            @"\mbrandy\M", @"\mcognac\M", @"\mliqueurs?\M", @"\msake\M", @"\mmirin\M", @"\mtequila\M", @"\mgin\M", @"\msherry\M", @"\mport\M", @"\mmadeira\M",
            @"\mmarsala\M", @"\mvermouth\M", @"\mciders?\M", @"\mchampagne\M", @"\mprosecco\M", @"\mclaret\M", @"\mkirsch\M", @"\mschnapps\M", @"\mspirits\M", @"\malcohol\M",
        };
        private static readonly string[] PorkOnly = { "%pork%", "%bacon%", "%ham%", "%lard%", "%prosciutto%", "%pancetta%", "%pepperoni%", "%salami%", "%chorizo%", "%gelatin%" };
        private static readonly string[] Lactose = { "%milk%", "%cream%", "%ice cream%", "%yogurt%", "%yoghurt%", "%whey%", "%kefir%", "%custard%", "%ricotta%", "%cottage cheese%", "%cream cheese%", "%mozzarella%" };
        private static readonly string[] Caffeine = { "%coffee%", "%espresso%", "%black tea%", "%green tea%", "%cola%", "%energy drink%", "%matcha%", "%yerba%", "%dark chocolate%" };
        private static readonly string[] Nightshade = { "%tomato%", "%potato%", "%eggplant%", "%aubergine%", "%bell pepper%", "%chili%", "%chile%", "%paprika%", "%cayenne%", "%jalape%", "%goji%", "%tomatillo%" };

        /// <summary>Restriction name (case-insensitive) → the nutrients whose stored presence the restriction forbids (by nutrient name; the diet gate by data, <see cref="DietCriteriaMatcher"/>).</summary>
        public static readonly IReadOnlyDictionary<string, string[]> NutrientsByName =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Halal"] = new[] { DietCriteriaMatcher.AlcoholNutrientName },
                ["No Alcohol"] = new[] { DietCriteriaMatcher.AlcoholNutrientName },
                ["Whole30"] = new[] { DietCriteriaMatcher.AlcoholNutrientName },
            };

        /// <summary>Restriction name (case-insensitive) → (severity, patterns).</summary>
        public static readonly IReadOnlyDictionary<string, (int Severity, string[] Patterns)> ByName =
            new Dictionary<string, (int, string[])>(StringComparer.OrdinalIgnoreCase)
            {
                // Allergies (5)
                ["Nut Allergy"] = (5, Peanut.Concat(TreeNuts).ToArray()),
                ["Peanut Allergy"] = (5, Peanut),
                ["Tree Nut Allergy"] = (5, TreeNuts),
                ["Almond Allergy"] = (5, new[] { "%almond%", "%marzipan%" }),
                ["Cashew Allergy"] = (5, new[] { "%cashew%" }),
                ["Walnut Allergy"] = (5, new[] { "%walnut%" }),
                ["Pecan Allergy"] = (5, new[] { "%pecan%", "%praline%" }),
                ["Pistachio Allergy"] = (5, new[] { "%pistachio%" }),
                ["Macadamia Allergy"] = (5, new[] { "%macadamia%" }),
                ["Brazil Nut Allergy"] = (5, new[] { "%brazil nut%" }),
                ["Hazelnut Allergy"] = (5, new[] { "%hazelnut%", "%nutella%", "%filbert%" }),
                ["Pine Nut Allergy"] = (5, new[] { "%pine nut%", "%pesto%" }),
                ["Egg Allergy"] = (5, Eggs),
                ["Milk Allergy"] = (5, Dairy),
                ["Soy Allergy"] = (5, Soy),
                ["Soybean Allergy"] = (5, Soy),
                ["Fish Allergy"] = (5, Fish),
                ["Shellfish Allergy"] = (5, Shellfish),
                ["Shrimp Allergy"] = (5, new[] { "%shrimp%", "%prawn%" }),
                ["Crab Allergy"] = (5, new[] { "%crab%" }),
                ["Lobster Allergy"] = (5, new[] { "%lobster%" }),
                ["Clam Allergy"] = (5, new[] { "%clam%" }),
                ["Mussel Allergy"] = (5, new[] { "%mussel%" }),
                ["Oyster Allergy"] = (5, new[] { "%oyster%" }),
                ["Scallop Allergy"] = (5, new[] { "%scallop%" }),
                ["Wheat Allergy"] = (5, new[] { "%wheat%", "%flour%", "%bread%", "%pasta%", "%couscous%", "%seitan%", "%bulgur%", "%farro%", "%semolina%", "%breadcrumb%", "%panko%", "%tortilla%", "%cracker%", "%spelt%", "%kamut%", "%triticale%", "%farina%", "%orzo%", "%gnocchi%" }),
                ["Sesame Allergy"] = (5, Sesame),
                ["Corn Allergy"] = (5, Corn),
                ["Coconut Allergy"] = (5, Coconut),
                ["Mustard Allergy"] = (5, Mustard),
                ["Celery Allergy"] = (5, Celery),
                ["Lupin Allergy"] = (5, new[] { "%lupin%" }),
                ["Buckwheat Allergy"] = (5, new[] { "%buckwheat%", "%soba%" }),
                ["Kiwi Allergy"] = (5, new[] { "%kiwi%" }),
                ["Banana Allergy"] = (5, new[] { "%banana%", "%plantain%" }),
                ["Avocado Allergy"] = (5, new[] { "%avocado%", "%guacamole%" }),
                ["Mango Allergy"] = (5, new[] { "%mango%" }),
                ["Strawberry Allergy"] = (5, new[] { "%strawberr%" }),
                ["Citrus Allergy"] = (5, new[] { "%lemon%", "%lime%", "%orange%", "%grapefruit%", "%tangerine%", "%clementine%", "%citrus%", "%yuzu%" }),
                // Intolerances / sensitivities (4)
                ["Lactose-Intolerant"] = (4, Lactose),
                ["Lactose Intolerance"] = (4, Lactose),
                ["Gluten Sensitivity"] = (4, Gluten),
                ["Celiac Disease"] = (4, Gluten),
                ["Sulfites Sensitivity"] = (4, new[] { "%wine%", "%dried fruit%", "%dried apricot%", "%raisin%", "%vinegar%", "%pickle%", "%sauerkraut%" }),
                ["Sulfite Sensitivity"] = (4, new[] { "%wine%", "%dried fruit%", "%dried apricot%", "%raisin%", "%vinegar%", "%pickle%", "%sauerkraut%" }),
                ["Caffeine Sensitivity"] = (4, Caffeine),
                ["Nightshade Sensitivity"] = (4, Nightshade),
                ["Nightshade-Free"] = (3, Nightshade),
                ["MSG Sensitivity"] = (4, new[] { "%msg%", "%monosodium glutamate%", "%bouillon%", "%soy sauce%" }),
                // Diets (3)
                ["Gluten-Free"] = (3, Gluten),
                ["Grain-Free"] = (3, Gluten.Concat(new[] { "%rice%", "%oat%", "%corn%", "%quinoa%", "%millet%", "%sorghum%", "%buckwheat%" }).ToArray()),
                ["Dairy-Free"] = (3, Dairy),
                ["Vegan"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(Dairy).Concat(Eggs).Concat(Honey).ToArray()),
                ["Ethical Vegan"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(Dairy).Concat(Eggs).Concat(Honey).ToArray()),
                ["Raw Vegan"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(Dairy).Concat(Eggs).Concat(Honey).ToArray()),
                ["Plant-Based"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(Dairy).Concat(Eggs).ToArray()),
                ["Vegetarian"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).ToArray()),
                ["Hindu Vegetarian"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(Eggs).ToArray()),
                ["Buddhist Vegetarian"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(new[] { "%garlic%", "%onion%", "%leek%", "%chive%", "%shallot%" }).ToArray()),
                ["Jain"] = (3, Meat.Concat(Poultry).Concat(Fish).Concat(Shellfish).Concat(Eggs).Concat(new[] { "%potato%", "%onion%", "%garlic%", "%carrot%", "%beet%", "%radish%", "%turnip%", "%ginger%", "%honey%" }).ToArray()),
                ["Pescatarian"] = (3, Meat.Concat(Poultry).ToArray()),
                ["Kosher"] = (3, PorkOnly.Concat(Shellfish).ToArray()),
                ["Halal"] = (3, PorkOnly.Concat(Alcohol).ToArray()),
                ["No Alcohol"] = (3, Alcohol),
                ["Sugar-Free"] = (3, new[] { "%sugar%", "%syrup%", "%honey%", "%candy%", "%chocolate%", "%soda%", "%cola%", "%jam%", "%jelly%", "%frosting%", "%icing%" }),
                ["Carnivore"] = (3, new[] { "%vegetable%", "%fruit%", "%grain%", "%rice%", "%bean%", "%lentil%", "%bread%", "%pasta%", "%potato%", "%sugar%" }),
                ["Paleo"] = (3, Gluten.Concat(Dairy).Concat(new[] { "%rice%", "%oat%", "%corn%", "%bean%", "%lentil%", "%chickpea%", "%peanut%", "%soy%", "%tofu%", "%sugar%" }).ToArray()),
                ["Whole30"] = (3, Gluten.Concat(Dairy).Concat(Alcohol).Concat(new[] { "%rice%", "%oat%", "%corn%", "%bean%", "%lentil%", "%chickpea%", "%peanut%", "%soy%", "%tofu%", "%sugar%", "%honey%", "%syrup%" }).ToArray()),
                ["Low-FODMAP"] = (3, new[] { "%onion%", "%garlic%", "%wheat%", "%rye%", "%apple%", "%pear%", "%watermelon%", "%honey%", "%milk%", "%yogurt%", "%bean%", "%lentil%", "%chickpea%", "%cauliflower%", "%mushroom%", "%cashew%", "%pistachio%" }),
                ["FODMAP Sensitivity"] = (4, new[] { "%onion%", "%garlic%", "%wheat%", "%rye%", "%apple%", "%pear%", "%watermelon%", "%honey%", "%milk%", "%yogurt%", "%bean%", "%lentil%", "%chickpea%", "%cauliflower%", "%mushroom%", "%cashew%", "%pistachio%" }),
            };

        /// <summary>
        /// Insert default criteria for every restriction reference (matched by name) that has
        /// none, and bring types holding only untouched defaults to the current defaults: add the
        /// patterns and nutrient rows added since, and remove the default rows no longer among them
        /// (the alcohol name patterns that became word patterns and a nutrient row). Returns the
        /// number of rows added. Safe to run on every startup; an earlier release puts back what it
        /// misses on its own start.
        /// </summary>
        public static async Task<int> EnsureAsync(ApplicationDbContext db, ILogger? logger = null)
        {
            var names = ByName.Keys.ToList();
            var refs = await db.Set<ReferenceEntity>()
                .Where(r => names.Contains(r.Name))
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();
            if (refs.Count == 0) return 0;

            var nutrientNames = NutrientsByName.Values.SelectMany(n => n).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var nutrients = (await db.Nutrients.AsNoTracking()
                    .Where(n => n.ParentNutrientId == null && nutrientNames.Contains(n.Name))
                    .Select(n => new { n.Id, n.Name })
                    .ToListAsync())
                .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(n => n.Id), StringComparer.OrdinalIgnoreCase);

            var refIds = refs.Select(r => r.Id).ToList();
            var existing = await db.Set<RestrictionCriterionEntity>()
                .IgnoreQueryFilters()
                .Where(c => refIds.Contains(c.RestrictionTypeId))
                .Select(c => new { c.Id, c.RestrictionTypeId, c.IngredientId, c.IngredientPattern, c.NutrientId, c.Notes, c.IsDeleted })
                .ToListAsync();
            var byType = existing.ToLookup(c => c.RestrictionTypeId);

            var now = DateTime.UtcNow;
            var added = 0;
            var typesTouched = 0;
            var retired = new List<long>();
            foreach (var r in refs)
            {
                if (!ByName.TryGetValue(r.Name, out var def)) continue;
                var rows = byType[r.Id].ToList();
                if (rows.Any(c => c.IsDeleted || c.Notes != DefaultNote)) continue;
                var patterns = def.Patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var nutrientIds = (NutrientsByName.TryGetValue(r.Name, out var wanted) ? wanted : Array.Empty<string>())
                    .Where(nutrients.ContainsKey).Select(n => nutrients[n]).Distinct().ToList();

                var stale = rows.Where(c => c.IngredientId == null
                    && (c.IngredientPattern != null ? !patterns.Contains(c.IngredientPattern, StringComparer.OrdinalIgnoreCase) : c.NutrientId == null || !nutrientIds.Contains(c.NutrientId.Value)))
                    .Select(c => c.Id)
                    .ToList();
                retired.AddRange(stale);

                var have = rows
                    .Where(c => c.IngredientPattern != null)
                    .Select(c => c.IngredientPattern!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var haveNutrients = rows.Where(c => c.IngredientPattern == null && c.NutrientId != null).Select(c => c.NutrientId!.Value).ToHashSet();
                var addedForType = 0;
                foreach (var pattern in patterns.Where(p => !have.Contains(p)))
                {
                    db.Set<RestrictionCriterionEntity>().Add(Row(r.Id, def.Severity, now, pattern: pattern));
                    addedForType++;
                }
                foreach (var nutrientId in nutrientIds.Where(n => !haveNutrients.Contains(n)))
                {
                    db.Set<RestrictionCriterionEntity>().Add(Row(r.Id, def.Severity, now, nutrientId: nutrientId));
                    addedForType++;
                }
                if (addedForType == 0 && stale.Count == 0) continue;
                added += addedForType;
                typesTouched++;
            }
            if (retired.Count > 0)
                db.Set<RestrictionCriterionEntity>().RemoveRange(await db.Set<RestrictionCriterionEntity>().IgnoreQueryFilters().Where(c => retired.Contains(c.Id)).ToListAsync());
            if (added > 0 || retired.Count > 0)
            {
                await db.SaveChangesAsync();
                logger?.LogInformation("Restriction criteria: added {Count} default rows and removed {Retired} superseded default rows for {Types} restriction type(s)", added, retired.Count, typesTouched);
            }
            return added;
        }

        private static RestrictionCriterionEntity Row(long typeId, int severity, DateTime now, string? pattern = null, long? nutrientId = null) => new()
        {
            RestrictionTypeId = typeId,
            IngredientPattern = pattern,
            NutrientId = nutrientId,
            Severity = severity,
            Notes = DefaultNote,
            CreatedDate = now,
            LastModifiedDate = now
        };
    }
}
