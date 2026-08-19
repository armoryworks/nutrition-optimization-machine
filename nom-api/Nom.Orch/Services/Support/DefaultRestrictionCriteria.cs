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
    /// Idempotent: a restriction type that already has criteria (seeded or admin-edited
    /// in Admin → Diet Categories) is left alone; only types with none get the defaults.
    /// Patterns are ILIKE against ingredient names and aliases. Severity 5 = allergy,
    /// 4 = medical/intolerance, 3 = diet/religious.
    /// </summary>
    public static class DefaultRestrictionCriteria
    {
        private static readonly string[] Meat = { "%beef%", "%pork%", "%lamb%", "%veal%", "%venison%", "%bison%", "%goat%", "%bacon%", "%ham%", "%sausage%", "%pepperoni%", "%salami%", "%prosciutto%", "%steak%", "%ground meat%", "%meatball%", "%hot dog%", "%chorizo%", "%pancetta%", "%lard%", "%gelatin%" };
        private static readonly string[] Poultry = { "%chicken%", "%turkey%", "%duck%", "%goose%", "%quail%", "%poultry%" };
        private static readonly string[] Fish = { "%salmon%", "%tuna%", "%cod%", "%tilapia%", "%trout%", "%halibut%", "%anchov%", "%sardine%", "%mackerel%", "%haddock%", "%bass%", "%snapper%", "%swordfish%", "%mahi%", "%fish%", "%catfish%", "%herring%", "%pollock%", "%flounder%", "%sole%" };
        private static readonly string[] Shellfish = { "%shrimp%", "%prawn%", "%crab%", "%lobster%", "%clam%", "%mussel%", "%oyster%", "%scallop%", "%crawfish%", "%crayfish%", "%squid%", "%calamari%", "%octopus%", "%shellfish%" };
        private static readonly string[] Dairy = { "%milk%", "%cheese%", "%butter%", "%yogurt%", "%yoghurt%", "%cream%", "%whey%", "%casein%", "%ghee%", "%kefir%", "%custard%", "%ice cream%", "%mozzarella%", "%cheddar%", "%parmesan%", "%feta%", "%ricotta%", "%brie%", "%gouda%" };
        private static readonly string[] Eggs = { "%egg%", "%mayonnaise%", "%meringue%", "%aioli%" };
        private static readonly string[] Honey = { "%honey%" };
        private static readonly string[] Gluten = { "%wheat%", "%flour%", "%barley%", "%rye%", "%bread%", "%pasta%", "%spaghetti%", "%noodle%", "%couscous%", "%seitan%", "%bulgur%", "%farro%", "%semolina%", "%cracker%", "%tortilla%", "%pita%", "%bagel%", "%croissant%", "%breadcrumb%", "%panko%", "%soy sauce%", "%malt%", "%beer%" };
        private static readonly string[] Peanut = { "%peanut%" };
        private static readonly string[] TreeNuts = { "%almond%", "%walnut%", "%cashew%", "%pecan%", "%pistachio%", "%hazelnut%", "%macadamia%", "%brazil nut%", "%pine nut%", "%chestnut%", "%tree nut%", "%mixed nut%", "%nut butter%", "%marzipan%", "%praline%", "%nutella%" };
        private static readonly string[] Soy = { "%soy%", "%tofu%", "%tempeh%", "%edamame%", "%miso%", "%tamari%", "%natto%" };
        private static readonly string[] Sesame = { "%sesame%", "%tahini%", "%halva%", "%hummus%" };
        private static readonly string[] Corn = { "%corn%", "%maize%", "%polenta%", "%grits%", "%popcorn%", "%tortilla%" };
        private static readonly string[] Coconut = { "%coconut%" };
        private static readonly string[] Mustard = { "%mustard%" };
        private static readonly string[] Celery = { "%celery%", "%celeriac%" };
        private static readonly string[] Alcohol = { "%wine%", "%beer%", "%vodka%", "%rum%", "%whisk%", "%bourbon%", "%brandy%", "%liqueur%", "%sake%", "%tequila%", "%gin%", "%sherry%", "%vermouth%", "%cider%" };
        private static readonly string[] PorkOnly = { "%pork%", "%bacon%", "%ham%", "%lard%", "%prosciutto%", "%pancetta%", "%pepperoni%", "%salami%", "%chorizo%", "%gelatin%" };
        private static readonly string[] Lactose = { "%milk%", "%cream%", "%ice cream%", "%yogurt%", "%yoghurt%", "%whey%", "%kefir%", "%custard%", "%ricotta%", "%cottage cheese%", "%cream cheese%", "%mozzarella%" };
        private static readonly string[] Caffeine = { "%coffee%", "%espresso%", "%black tea%", "%green tea%", "%cola%", "%energy drink%", "%matcha%", "%yerba%", "%dark chocolate%" };
        private static readonly string[] Nightshade = { "%tomato%", "%potato%", "%eggplant%", "%aubergine%", "%bell pepper%", "%chili%", "%chile%", "%paprika%", "%cayenne%", "%jalape%", "%goji%", "%tomatillo%" };

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
                ["Wheat Allergy"] = (5, new[] { "%wheat%", "%flour%", "%bread%", "%pasta%", "%couscous%", "%seitan%", "%bulgur%", "%farro%", "%semolina%", "%breadcrumb%", "%panko%", "%tortilla%", "%cracker%" }),
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
        /// none. Returns the number of rows added. Safe to run on every startup.
        /// </summary>
        public static async Task<int> EnsureAsync(ApplicationDbContext db, ILogger? logger = null)
        {
            var names = ByName.Keys.ToList();
            var refs = await db.Set<ReferenceEntity>()
                .Where(r => names.Contains(r.Name))
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();
            if (refs.Count == 0) return 0;

            var refIds = refs.Select(r => r.Id).ToList();
            var covered = await db.Set<RestrictionCriterionEntity>()
                .Where(c => refIds.Contains(c.RestrictionTypeId))
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync();
            var coveredSet = covered.ToHashSet();

            var now = DateTime.UtcNow;
            var added = 0;
            foreach (var r in refs)
            {
                if (coveredSet.Contains(r.Id)) continue;
                if (!ByName.TryGetValue(r.Name, out var def)) continue;
                foreach (var pattern in def.Patterns.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity
                    {
                        RestrictionTypeId = r.Id,
                        IngredientPattern = pattern,
                        Severity = def.Severity,
                        Notes = "default (name-keyed baseline)",
                        CreatedDate = now,
                        LastModifiedDate = now
                    });
                    added++;
                }
            }
            if (added > 0)
            {
                await db.SaveChangesAsync();
                logger?.LogInformation("Restriction criteria: added {Count} default rows for {Types} restriction type(s) that had none", added, refs.Count(r => !coveredSet.Contains(r.Id)));
            }
            return added;
        }
    }
}
