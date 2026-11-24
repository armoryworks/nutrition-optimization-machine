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
    /// <summary>A pattern a restriction type means beside another of its patterns, because that pattern's named exceptions would clear it or because it names the same food in another spelling or a wider name, with the reason.</summary>
    public sealed record RestrictionCompanion(string Type, string Pattern, string Companion, string Reason);

    /// <summary>
    /// Baseline <see cref="RestrictionCriterionEntity"/> rows for the common allergies,
    /// intolerances, diets and religious rules, keyed by restriction *name* so both the
    /// legacy (2000-series) and current (60xxx) reference sets — and any tenant — resolve.
    /// Idempotent: a restriction type whose criteria were seeded or admin-edited in
    /// Admin → Diet Categories (any non-default or deleted row) is left alone; types with
    /// none, or with only untouched defaults, get whichever default patterns they lack. Every type,
    /// seeded and edited ones too, gets the <see cref="Companions"/> of the patterns it holds.
    /// Patterns with <c>%</c> are read by words against ingredient names, aliases and recipe lines
    /// (<see cref="DietPatternRule"/>: "graham" is not ham), except word patterns
    /// (<c>\mrum\M</c>), which match whole words, and the alcohol diets also carry a nutrient
    /// row (<see cref="NutrientsByName"/>, capped per serving for the types in <see cref="MaxPerServingByName"/>;
    /// both read by <see cref="DietCriteriaMatcher"/>).
    /// The wheat, gluten, milk, egg, soy, peanut, tree nut, fish, shellfish, sesame and sulfite patterns carry their class's
    /// derived and cited vocabulary (<see cref="DietVocabulary"/>, decided in), so every type that holds the class holds it.
    /// Severity 5 = allergy, 4 = medical/intolerance, 3 = diet/religious.
    /// </summary>
    public static class DefaultRestrictionCriteria
    {
        internal const string DefaultNote = "default (name-keyed baseline)";

        private static readonly string[] Meat = { "%beef%", "%pork%", "%lamb%", "%veal%", "%venison%", "%bison%", "%goat%", "%bacon%", "%ham%", "%sausage%", "%pepperoni%", "%salami%", "%prosciutto%", "%steak%", "%ground meat%", "%meatball%", "%hot dog%", "%chorizo%", "%pancetta%", "%lard%", "%gelatin%" };
        private static readonly string[] Poultry = { "%chicken%", "%turkey%", "%duck%", "%goose%", "%quail%", "%poultry%" };
        private static readonly string[] Fish = new[] { "%salmon%", "%tuna%", "%cod%", "%tilapia%", "%trout%", "%halibut%", "%anchov%", "%sardine%", "%mackerel%", "%haddock%", "%bass%", "%snapper%", "%swordfish%", "%mahi%", "%fish%", "%catfish%", "%herring%", "%pollock%", "%flounder%", "%sole%" }.Concat(DietVocabulary.PatternsOf("fish")).ToArray();
        private static readonly string[] Shellfish = new[] { "%shrimp%", "%prawn%", "%crab%", "%lobster%", "%clam%", "%mussel%", "%oyster%", "%scallop%", "%crawfish%", "%crayfish%", "%squid%", "%calamari%", "%octopus%", "%shellfish%" }.Concat(DietVocabulary.PatternsOf("shellfish")).ToArray();
        private static readonly string[] Dairy = new[] { "%milk%", "%cheese%", "%butter%", "%yogurt%", "%yoghurt%", "%cream%", "%whey%", "%casein%", "%ghee%", "%kefir%", "%custard%", "%ice cream%", "%mozzarella%", "%cheddar%", "%parmesan%", "%feta%", "%ricotta%", "%brie%", "%gouda%" }.Concat(DietVocabulary.PatternsOf("milk")).ToArray();
        private static readonly string[] Eggs = new[] { "%egg%", "%mayonnaise%", "%meringue%", "%aioli%" }.Concat(DietVocabulary.PatternsOf("egg")).ToArray();
        private static readonly string[] Honey = { "%honey%" };
        private static readonly string[] Gluten = new[] { "%wheat%", "%flour%", "paste", "%barley%", "%rye%", "%bread%", "%pasta%", "%spaghetti%", "%noodle%", "%couscous%", "%seitan%", "%bulgur%", "%farro%", "%semolina%", "%cracker%", "%tortilla%", "%pita%", "%bagel%", "%croissant%", "%breadcrumb%", "%panko%", "%soy sauce%", "%malt%", "%beer%", "oat%", "% oat%", "%granola%", "%muesli%", "%spelt%", "%kamut%", "%triticale%", "%farina%", "%orzo%", "%gnocchi%" }.Concat(DietVocabulary.PatternsOf("wheat")).Concat(DietVocabulary.PatternsOf("gluten")).ToArray();
        private static readonly string[] Peanut = new[] { "%peanut%" }.Concat(DietVocabulary.PatternsOf("peanut")).ToArray();
        private static readonly string[] TreeNuts = new[] { "%almond%", "%walnut%", "%cashew%", "%pecan%", "%pistachio%", "%hazelnut%", "%macadamia%", "%brazil nut%", "%pine nut%", "%chestnut%", "%tree nut%", "%mixed nut%", "%nut butter%", "%marzipan%", "%praline%", "%nutella%" }.Concat(DietVocabulary.PatternsOf("treenut")).ToArray();
        private static readonly string[] Soy = new[] { "%soy%", "%tofu%", "%tempeh%", "%edamame%", "%miso%", "%tamari%", "%natto%" }.Concat(DietVocabulary.PatternsOf("soy")).ToArray();
        private static readonly string[] Sesame = new[] { "%sesame%", "%tahini%", "%halva%", "%hummus%" }.Concat(DietVocabulary.PatternsOf("sesame")).ToArray();
        private static readonly string[] Sulfite = new[] { "%wine%", "%dried fruit%", "%dried apricot%", "%raisin%", "%vinegar%", "%pickle%", "%sauerkraut%" }.Concat(DietVocabulary.PatternsOf("sulfite")).ToArray();
        private static readonly string[] WheatOnly = new[] { "%wheat%", "%flour%", "paste", "%bread%", "%pasta%", "%couscous%", "%seitan%", "%bulgur%", "%farro%", "%semolina%", "%breadcrumb%", "%panko%", "%tortilla%", "%cracker%", "%spelt%", "%kamut%", "%triticale%", "%farina%", "%orzo%", "%gnocchi%" }.Concat(DietVocabulary.PatternsOf("wheat")).ToArray();
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

        /// <summary>
        /// Restriction name (case-insensitive) → the per-serving cap its nutrient rows carry (owner answer 6): No Alcohol
        /// excludes a recipe only when more than 0.5 g of its drinks' alcohol stays per serving after cooking, or when a
        /// drink line cannot be counted. A restriction not named here forbids the nutrient's presence: Halal and Whole30
        /// exclude a recipe with any drink, however long it cooks. Every alcohol diet leaves flavourings to a note.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, decimal> MaxPerServingByName =
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["No Alcohol"] = 0.5m,
            };

        private static readonly string[] MeatTypes = { "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based", "Vegetarian", "Hindu Vegetarian", "Buddhist Vegetarian", "Jain", "Pescatarian" };
        private static readonly string[] SugarGrainTypes = { "Paleo", "Whole30" };
        private static readonly string[] FodmapTypes = { "Low-FODMAP", "FODMAP Sensitivity" };
        private static readonly string[] FatTypes = { "Pancreatitis", "Gallbladder Disease" };
        private static readonly string[] VeganTypes = { "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based" };
        private static readonly string[] ShellfishMeatTypes = VeganTypes.Concat(new[] { "Vegetarian", "Hindu Vegetarian", "Buddhist Vegetarian", "Jain" }).ToArray();
        private static readonly string[] WineTypes = { "Sulfite Sensitivity", "Sulfites Sensitivity", "Histamine Intolerance" };
        private static readonly string[] FlourTypes = { "Wheat Allergy", "Celiac Disease", "Gluten Sensitivity", "Gluten-Free", "Grain-Free", "Paleo", "Whole30" };

        private static IEnumerable<RestrictionCompanion> Companion(IEnumerable<string> types, string pattern, string reason, params string[] companions) =>
            from t in types from c in companions select new RestrictionCompanion(t, pattern, c, reason);

        /// <summary>
        /// The patterns a restriction type means beside one of its patterns whose named exceptions
        /// (<see cref="DietPatternRule.All"/>) would clear a compound the type restricts: a type that
        /// holds the pattern also holds its companions. They are part of the defaults
        /// (<see cref="ByName"/>), and <see cref="EnsureAsync"/> adds them to a type that holds the
        /// pattern however its criteria were seeded or edited (db/seed.sql's Pancreatitis and
        /// Gallbladder Disease, an admin-edited Vegetarian), unless an admin deleted the companion.
        /// </summary>
        public static readonly IReadOnlyList<RestrictionCompanion> Companions =
            Companion(MeatTypes, "%ham%", "hamburger is chopped beef (USDA, 9 CFR 319.15(b)), which the exception of %ham% clears", "%hamburger%")
            .Concat(Companion(SugarGrainTypes, "%wheat%", "buckwheat is a pseudo-cereal these diets exclude with the grains, which the exception of %wheat% clears", "%buckwheat%"))
            .Concat(Companion(SugarGrainTypes, "%beer%", "root, ginger and birch beer are sweetened soft drinks these diets exclude as added sugar, which the exceptions of %beer% clear", "%root beer%", "%ginger beer%", "%birch beer%"))
            .Concat(Companion(SugarGrainTypes, "%malt%", "maltodextrin is an added sugar on the Whole30 list of sugar names, which the exception of %malt% clears", "%maltodextrin%"))
            .Concat(Companion(new[] { "Grain-Free" }, "%malt%", "maltodextrin is a starch made from corn, wheat or rice, which the exception of %malt% clears", "%maltodextrin%"))
            .Concat(Companion(SugarGrainTypes.Append("Grain-Free").Append("Carnivore"), "%rice%", "licorice candy is made with wheat flour and sugar, which the exception of %rice% clears", "%licorice%", "%liquorice%"))
            .Concat(Companion(FodmapTypes, "%honey%", "Honeycrisp is an apple, high in fructose and sorbitol (Monash University FODMAP research), which the exception of %honey% clears", "%honeycrisp%"))
            .Concat(Companion(FodmapTypes, "%pear%", "barley is high in fructans (Monash University FODMAP research), which the exception of %pear% clears in pearl barley", "%pearl barley%"))
            .Concat(Companion(new[] { "Jain" }, "%egg%", "many Jains avoid brinjal (eggplant) as a many-seeded vegetable (bahubija), which the exception of %egg% clears", "%eggplant%"))
            .Concat(Companion(VeganTypes, "%fish%", "Goldfish crackers are baked with cheddar cheese, which these types exclude as dairy; the exception of %fish% clears them", "%goldfish cracker%"))
            .Concat(Companion(ShellfishMeatTypes, "%scallop%", "scallopini are thin veal cutlets, which these types exclude as meat; the exception of %scallop% clears them", "%scallopini%"))
            .Concat(Companion(FodmapTypes, "%milk%", "soy drinks of whole soybeans and oat drinks are high in FODMAPs at a usual serve (Monash University FODMAP research), which the exceptions of %milk% clear", "%soy milk%", "%soymilk%", "%oat milk%"))
            .Concat(Companion(FodmapTypes, "%yogurt%", "soy yogurt of whole soybeans is high in FODMAPs (Monash University FODMAP research), which the exception of %yogurt% clears", "%soy yogurt%"))
            .Concat(Companion(new[] { "Pancreatitis" }, "%butter%", "nut, seed and cocoa butters are among the fattiest foods, restricted here for fat, not dairy; the exceptions of %butter% clear them", "%peanut butter%", "%almond butter%", "%cashew butter%", "%nut butter%", "%seed butter%", "%sunflower butter%", "%cocoa butter%", "%cacao butter%"))
            .Concat(Companion(FatTypes, "%bacon%", "turkey and beef bacon are cured fatty meats, restricted here for fat, not pork; the exceptions of %bacon% clear them", "%turkey bacon%", "%beef bacon%"))
            .Concat(Companion(FlourTypes, "%flour%", "flower is the old spelling of flour in seventeenth- to nineteenth-century cookbooks (staging checkpoint 4, P12); the blossoms are named exceptions of %flower%", "%flower%"))
            .Concat(Companion(WineTypes, "%wine%", "sherry, port, Madeira, Marsala, claret, champagne and the other semi-generic type names are wines (27 CFR 4.21 and 4.24(b)(2)), vermouth an aperitif wine (27 CFR 4.21(g)) and Shaoxing a rice wine (27 CFR 4.21(f)), which these types restrict as wine though the line does not say wine (staging checkpoint 5, Q13)", "%sherry%", "%port%", "%madeira%", "%marsala%", "%vermouth%", "%claret%", "%champagne%", "%prosecco%", "%sauterne%", "%chablis%", "%chianti%", "%burgundy%", "%moselle%", "%tokay%", "%shaoxing%"))
            .Concat(Companion(WineTypes, "%wine%", "hard cider is fermented apple juice, a fruit wine (27 CFR 4.21(e)) sulfited and aged like wine (staging checkpoint 5, Q13); sweet cider is juice and is not read", "%hard cider%"))
            .Concat(Companion(new[] { "Peanut Allergy" }, "%peanut%", "a nut butter that does not name its nut may be peanut butter, sold and named among the nut butters though peanuts are legumes (staging checkpoint 4, P3); peanut butter itself is held by %peanut%", "%nut butter%"))
            .ToList();

        /// <summary>The note a companion pattern carries when <see cref="EnsureAsync"/> adds it to a type that does not hold untouched defaults.</summary>
        internal static string CompanionNote(string pattern) => "companion of " + pattern + " (" + DietPatternRule.Version + ")";

        /// <summary>The note a composite food's pattern carries when <see cref="EnsureAsync"/> adds it beside the pattern that restricts what the food is made of.</summary>
        internal static string CompositeNote(string pattern) => "composite food, by " + pattern + " (" + StatementLabelTerms.Version + ")";

        private static IReadOnlyDictionary<string, (int Severity, string[] Patterns)> WithCompanions(IReadOnlyDictionary<string, (int Severity, string[] Patterns)> types) =>
            types.ToDictionary(kv => kv.Key, kv => (kv.Value.Severity, kv.Value.Patterns.Concat(
                    Companions.Where(c => string.Equals(c.Type, kv.Key, StringComparison.OrdinalIgnoreCase) && kv.Value.Patterns.Contains(c.Pattern, StringComparer.OrdinalIgnoreCase)).Select(c => c.Companion))
                    .Concat(CompositeCompanions.Where(c => string.Equals(c.Type, kv.Key, StringComparison.OrdinalIgnoreCase)).Select(c => c.Companion))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()), StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, (int Severity, string[] Patterns)> Base =
            new(StringComparer.OrdinalIgnoreCase)
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
                ["Wheat Allergy"] = (5, WheatOnly),
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
                ["Sulfites Sensitivity"] = (4, Sulfite),
                ["Sulfite Sensitivity"] = (4, Sulfite),
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
        /// The composite foods a type means because it restricts what they are made of (staging checkpoint 3: Milk
        /// Allergy and Dairy-Free never read "Goldfish crackers", the gluten types never read "licorice" candy): for each
        /// composite food of the cited table (<see cref="StatementLabelTerms.Composites"/>) and each dish of the derived layer
        /// (<see cref="DietDishes.Composites"/>, staging checkpoint 6, R5), a type one of whose patterns
        /// names one of its contents ("cheddar cheese", "wheat flour") holds the composite's patterns, at that pattern's
        /// severity, unless a pattern of its own already names the composite. The same reading serves a type of any
        /// name (<see cref="EnsureAsync"/>), so an admin's own type gets them from its own patterns.
        /// </summary>
        public static IEnumerable<RestrictionCompanion> CompositeCompanionsFor(string type, IEnumerable<string> patterns)
        {
            var likes = patterns.Where(p => !string.IsNullOrWhiteSpace(p) && !DietCriteriaMatcher.IsWordPattern(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var composite in StatementLabelTerms.Composites.Concat(DietDishes.Composites))
            {
                foreach (var pattern in StatementLabelTerms.PatternsOf(composite))
                {
                    var phrase = "1 cup " + pattern.Trim('%');
                    if (likes.Any(p => DietPatternRule.For(p, type).IsMatch(phrase))) continue;
                    var basis = likes.Select(p => (Pattern: p, Word: composite.Contains.FirstOrDefault(w => DietPatternRule.For(p, type).IsMatch("1 cup " + w))))
                        .FirstOrDefault(x => x.Word != null);
                    if (basis.Word == null) continue;
                    yield return new RestrictionCompanion(type, basis.Pattern, pattern,
                        $"{composite.Term} is made with {string.Join(", ", composite.Contains)}, and {basis.Pattern} restricts {basis.Word} ({composite.Source})");
                }
            }
        }

        /// <summary>The <see cref="CompositeCompanionsFor"/> of every default type, beside its <see cref="Companions"/>; part of <see cref="ByName"/>.</summary>
        public static readonly IReadOnlyList<RestrictionCompanion> CompositeCompanions = Base
            .SelectMany(kv => CompositeCompanionsFor(kv.Key, kv.Value.Patterns.Concat(Companions.Where(c => string.Equals(c.Type, kv.Key, StringComparison.OrdinalIgnoreCase) && kv.Value.Patterns.Contains(c.Pattern, StringComparer.OrdinalIgnoreCase)).Select(c => c.Companion))))
            .ToList();

        /// <summary>Restriction name (case-insensitive) → (severity, patterns), with each type's <see cref="Companions"/> and <see cref="CompositeCompanions"/>.</summary>
        public static readonly IReadOnlyDictionary<string, (int Severity, string[] Patterns)> ByName = WithCompanions(Base);

        /// <summary>
        /// The default types an admin's own type was copied from: the types none of whose own patterns it lacks, the
        /// largest of them (several when they hold the same patterns, as Vegan and Ethical Vegan do). A type named as a
        /// default or seeded type, or one that holds no default type whole, has none.
        /// </summary>
        public static IReadOnlyList<string> OriginsOf(string type, IEnumerable<string> patterns)
        {
            if (Base.ContainsKey(type) || Companions.Any(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))) return Array.Empty<string>();
            var held = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var whole = Base.Where(kv => kv.Value.Patterns.All(held.Contains)).ToList();
            if (whole.Count == 0) return Array.Empty<string>();
            var most = whole.Max(kv => kv.Value.Patterns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            return whole.Where(kv => kv.Value.Patterns.Distinct(StringComparer.OrdinalIgnoreCase).Count() == most).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        /// <summary>The companions a type holding <paramref name="patterns"/> is given: its own (<see cref="Companions"/>), those of the default types an admin's type was copied from (<see cref="OriginsOf"/>), and its composite foods (<see cref="CompositeCompanionsFor"/>).</summary>
        public static IEnumerable<RestrictionCompanion> CompanionsFor(string type, IReadOnlyCollection<string> patterns)
        {
            var origins = OriginsOf(type, patterns);
            var own = Companions.Where(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)
                    || origins.Contains(c.Type, StringComparer.OrdinalIgnoreCase))
                .Select(c => c with { Type = type })
                .DistinctBy(c => (c.Pattern.ToLowerInvariant(), c.Companion.ToLowerInvariant()))
                .ToList();
            var held = patterns.Concat(own.Where(c => patterns.Contains(c.Pattern, StringComparer.OrdinalIgnoreCase)).Select(c => c.Companion)).ToList();
            return own.Concat(CompositeCompanionsFor(type, held));
        }

        /// <summary>
        /// Insert default criteria for every restriction reference (matched by name) that has
        /// none, and bring types holding only untouched defaults to the current defaults: add the
        /// patterns and nutrient rows added since, and remove the default rows no longer among them
        /// (the alcohol name patterns that became word patterns and a nutrient row, and a nutrient row
        /// whose per-serving cap is no longer the default's). Then give every type, seeded or edited
        /// too, the <see cref="Companions"/> of the patterns it holds, at the severity of the pattern,
        /// unless the type has a row for the companion (an admin's deleted one included). Returns the
        /// number of rows added. Safe to run on every startup; an earlier release puts back what it
        /// misses on its own start.
        /// </summary>
        public static async Task<int> EnsureAsync(ApplicationDbContext db, ILogger? logger = null)
        {
            var names = ByName.Keys.Concat(Companions.Select(c => c.Type)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var withPatterns = await db.Set<RestrictionCriterionEntity>()
                .Where(c => c.IngredientPattern != null && !c.IsDeleted)
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync();
            var refs = await db.Set<ReferenceEntity>()
                .Where(r => names.Contains(r.Name) || withPatterns.Contains(r.Id))
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
                .Select(c => new { c.Id, c.RestrictionTypeId, c.IngredientId, c.IngredientPattern, c.NutrientId, c.MaxAmountPerServing, c.Severity, c.Notes, c.IsDeleted })
                .ToListAsync();
            var byType = existing.ToLookup(c => c.RestrictionTypeId);

            var now = DateTime.UtcNow;
            var added = 0;
            var typesTouched = 0;
            var retired = new List<long>();
            var planned = new HashSet<(long, string)>();
            var leftAlone = new List<string>();
            foreach (var r in refs)
            {
                if (!ByName.TryGetValue(r.Name, out var def)) continue;
                var rows = byType[r.Id].ToList();
                if (rows.Any(c => c.IsDeleted || c.Notes != DefaultNote))
                {
                    leftAlone.Add(r.Name);
                    continue;
                }
                var patterns = def.Patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var nutrientIds = (NutrientsByName.TryGetValue(r.Name, out var wanted) ? wanted : Array.Empty<string>())
                    .Where(nutrients.ContainsKey).Select(n => nutrients[n]).Distinct().ToList();
                decimal? cap = MaxPerServingByName.TryGetValue(r.Name, out var max) ? max : null;

                var stale = rows.Where(c => c.IngredientId == null
                    && (c.IngredientPattern != null ? !patterns.Contains(c.IngredientPattern, StringComparer.OrdinalIgnoreCase) : c.NutrientId == null || !nutrientIds.Contains(c.NutrientId.Value) || c.MaxAmountPerServing != cap))
                    .Select(c => c.Id)
                    .ToList();
                retired.AddRange(stale);

                var have = rows
                    .Where(c => c.IngredientPattern != null)
                    .Select(c => c.IngredientPattern!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var haveNutrients = rows.Where(c => c.IngredientPattern == null && c.NutrientId != null && c.MaxAmountPerServing == cap).Select(c => c.NutrientId!.Value).ToHashSet();
                var addedForType = 0;
                foreach (var pattern in patterns.Where(p => !have.Contains(p)))
                {
                    db.Set<RestrictionCriterionEntity>().Add(Row(r.Id, def.Severity, now, pattern: pattern));
                    planned.Add((r.Id, pattern.ToLowerInvariant()));
                    addedForType++;
                }
                foreach (var nutrientId in nutrientIds.Where(n => !haveNutrients.Contains(n)))
                {
                    db.Set<RestrictionCriterionEntity>().Add(Row(r.Id, def.Severity, now, nutrientId: nutrientId, maxPerServing: cap));
                    addedForType++;
                }
                if (addedForType == 0 && stale.Count == 0) continue;
                added += addedForType;
                typesTouched++;
            }
            var companions = 0;
            var companionTypes = new List<string>();
            var copied = new List<string>();
            foreach (var r in refs)
            {
                var rows = byType[r.Id].ToList();
                var live = rows.Where(x => !x.IsDeleted && x.IngredientId == null && x.NutrientId == null && x.IngredientPattern != null).ToList();
                var held = live.Select(x => x.IngredientPattern!.Trim()).Concat(planned.Where(p => p.Item1 == r.Id).Select(p => p.Item2)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var origins = OriginsOf(r.Name, held);
                if (origins.Count > 0) copied.Add($"{r.Name} (as {string.Join(" / ", origins)})");
                var addedForType = 0;
                var severities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var x in live) severities[x.IngredientPattern!.Trim()] = Math.Max(x.Severity, severities.GetValueOrDefault(x.IngredientPattern!.Trim()));
                if (ByName.TryGetValue(r.Name, out var def))
                    foreach (var p in planned.Where(p => p.Item1 == r.Id).Select(p => p.Item2)) severities.TryAdd(p, def.Severity);
                for (var round = 0; round < 2; round++)
                {
                    foreach (var c in CompanionsFor(r.Name, held.ToList()).ToList())
                    {
                        if (!severities.TryGetValue(c.Pattern, out var severity) || planned.Contains((r.Id, c.Companion.ToLowerInvariant()))) continue;
                        if (rows.Any(x => string.Equals(x.IngredientPattern?.Trim(), c.Companion, StringComparison.OrdinalIgnoreCase))) continue;
                        var row = Row(r.Id, severity, now, pattern: c.Companion);
                        row.Notes = Companions.Any(x => string.Equals(x.Companion, c.Companion, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Pattern, c.Pattern, StringComparison.OrdinalIgnoreCase))
                            ? CompanionNote(c.Pattern) : CompositeNote(c.Pattern);
                        db.Set<RestrictionCriterionEntity>().Add(row);
                        planned.Add((r.Id, c.Companion.ToLowerInvariant()));
                        held.Add(c.Companion);
                        severities.TryAdd(c.Companion, severity);
                        addedForType++;
                    }
                }
                if (addedForType == 0) continue;
                companions += addedForType;
                companionTypes.Add(r.Name);
            }
            if (retired.Count > 0)
                db.Set<RestrictionCriterionEntity>().RemoveRange(await db.Set<RestrictionCriterionEntity>().IgnoreQueryFilters().Where(c => retired.Contains(c.Id)).ToListAsync());
            if (added > 0 || retired.Count > 0 || companions > 0)
            {
                await db.SaveChangesAsync();
                logger?.LogInformation("Restriction criteria: added {Count} default rows and removed {Retired} superseded default rows for {Types} restriction type(s); added {Companions} companion pattern(s) to {CompanionTypes}; left {LeftAlone} seeded or edited type(s) without default top-up: {LeftAloneNames}; admin types read as copies of a default: {Copied}",
                    added, retired.Count, typesTouched, companions, companionTypes.Count == 0 ? "no type" : string.Join(", ", companionTypes.Distinct()), leftAlone.Count, string.Join(", ", leftAlone.Distinct()), copied.Count == 0 ? "none" : string.Join(", ", copied.Distinct()));
            }
            return added + companions;
        }

        private static RestrictionCriterionEntity Row(long typeId, int severity, DateTime now, string? pattern = null, long? nutrientId = null, decimal? maxPerServing = null) => new()
        {
            RestrictionTypeId = typeId,
            IngredientPattern = pattern,
            NutrientId = nutrientId,
            MaxAmountPerServing = maxPerServing,
            Severity = severity,
            Notes = DefaultNote,
            CreatedDate = now,
            LastModifiedDate = now
        };
    }
}
