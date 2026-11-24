using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// How the diet gate's build job reads recipe lines (<see cref="DietLineRules"/>): on generated data of the
    /// production copy's size and shape (843,636 live lines over 122,580 recipes) every default type's rules read every
    /// line within minutes, and the reading is the matcher's own, line by line; a measure named after a drink is read as
    /// a measure.
    /// </summary>
    [Collection(DietReadingRegisterTests.RunsAlone)]
    public class DietLineRulesTests
    {
        private const long HouseholdId = 7, PersonId = 70, SulfiteRef = 60801;
        private const int ProductionLines = 843_636, ProductionRecipes = 122_580;

        private readonly ITestOutputHelper _output;

        public DietLineRulesTests(ITestOutputHelper output) => _output = output;

        private static ApplicationDbContext NewContext(string? name = null) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        [Fact]
        public void A_prepared_meal_is_held_while_the_foods_are_not_known()
        {
            var set = new RestrictedIngredientSet { Pending = true, HasSevere = true };
            var row = new IngredientContentsEntity { IngredientId = 5, Outcome = IngredientContentsOutcome.Read, IngredientsJson = "[]" };

            var verdict = PreparedContentsGate.Judge(5, new[] { row }, HouseholdId, set, new HashSet<long>());

            verdict.Offered.Should().BeFalse();
            verdict.Held.Should().Be(PreparedContentsHeld.Pending);
        }

        [Fact]
        public async Task An_alias_made_of_a_measure_named_after_a_drink_puts_no_staple_under_the_drink()
        {
            using var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = SulfiteRef, Name = "Sulfite Sensitivity" });
            var salt = new IngredientEntity { Name = "Salt", CurationStatusId = (long)CurationStatusEnum.Curated };
            salt.Aliases.Add(new IngredientAliasEntity { AliasName = "wine glassful of salt", SourceContext = "deterministic:recheck:name-key" });
            var water = new IngredientEntity { Name = "Water", CurationStatusId = (long)CurationStatusEnum.Curated };
            water.Aliases.Add(new IngredientAliasEntity { AliasName = "Wineglass Water", SourceContext = "deterministic:recheck:staple" });
            water.Aliases.Add(new IngredientAliasEntity { AliasName = "wineglassful of warm water", SourceContext = "deterministic:recheck:staple" });
            var cooking = new IngredientEntity { Name = "Cooking Liquid", CurationStatusId = (long)CurationStatusEnum.Curated };
            cooking.Aliases.Add(new IngredientAliasEntity { AliasName = "dry white wine", SourceContext = "admin:7" });
            db.Ingredients.AddRange(salt, water, cooking);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Sulfite Sensitivity", RestrictionTypeId = SulfiteRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            set.IngredientIds.Should().NotContain(new[] { salt.Id, water.Id }, "a wine glass is a measure, read as one word and never as wine (staging checkpoint 4, P5)");
            set.IngredientIds.Should().Contain(cooking.Id, "an alias that names wine itself still counts");
            DietCriteriaMatcher.AliasText("Wineglass Water").Trim().Should().Be("Water");
            DietMeasureWords.Holds("one wine-glassful of brandy").Should().BeTrue();
            DietMeasureWords.Holds("a teacupsful of milk").Should().BeTrue();
            DietMeasureWords.Holds("1 cup dry white wine").Should().BeFalse();
            DietMeasureWords.Holds("winegrapes").Should().BeFalse();
        }

        [Fact]
        public async Task A_wine_cup_named_as_a_punch_is_a_drink_and_only_a_tea_coffee_or_breakfast_cup_is_a_measure()
        {
            using var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = SulfiteRef, Name = "Sulfite Sensitivity" });
            var punch = new IngredientEntity { Name = "Party Punch", CurationStatusId = (long)CurationStatusEnum.Curated };
            punch.Aliases.Add(new IngredientAliasEntity { AliasName = "Wine Cup", SourceContext = "admin:7" });
            db.Ingredients.Add(punch);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Sulfite Sensitivity", RestrictionTypeId = SulfiteRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            set.IngredientIds.Should().Contain(punch.Id, "a wine cup is a punch made of wine, not a measure (review CK9S-3)");
            foreach (var punchName in new[] { "Claret Cup", "champagne cup", "Sherry Cup", "brandy cups", "Beer Cup", "cider cup" })
                DietMeasureWords.Holds(punchName).Should().BeFalse(punchName + " names a drink");
            DietCriteriaMatcher.AliasText("Champagne Cup").Should().Contain("Champagne");
            foreach (var measure in new[] { "wine glassful", "a sherry-glass", "brandy glasses", "teacupful", "coffee cup", "breakfast cupful", "saltspoon", "salt-spoonful" })
                DietMeasureWords.Holds(measure).Should().BeTrue(measure + " is a measure");
        }

        private static List<DietDataCriterion> DefaultCriteria()
        {
            var criteria = new List<DietDataCriterion>();
            long id = 1, typeId = 100;
            foreach (var (name, (severity, patterns)) in DefaultRestrictionCriteria.ByName)
            {
                typeId++;
                foreach (var p in patterns.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var word = DietCriteriaMatcher.IsWordPattern(p);
                    criteria.Add(new DietDataCriterion(id++, typeId, word ? p.Trim() : null, null, null, null, severity, null, word ? null : p.Trim(), name));
                }
                if (DefaultRestrictionCriteria.NutrientsByName.TryGetValue(name, out var nutrients) && nutrients.Any(DietCriteriaMatcher.IsAlcohol))
                    criteria.Add(new DietDataCriterion(id++, typeId, null, 5038, DietCriteriaMatcher.AlcoholNutrientName,
                        DefaultRestrictionCriteria.MaxPerServingByName.TryGetValue(name, out var cap) ? cap : null, severity, null, null, name));
            }
            return criteria;
        }

        private static readonly string[] Foods =
        {
            "all-purpose flour", "whole wheat flour", "bread flour", "unsalted butter", "salted butter", "butter, softened", "large eggs", "egg yolks",
            "egg whites, beaten stiff", "whole milk", "buttermilk", "heavy cream", "sour cream", "cream cheese", "cheddar cheese, grated", "parmesan cheese",
            "granulated sugar", "brown sugar, packed", "powdered sugar", "honey", "maple syrup", "kosher salt", "salt and pepper to taste", "black pepper",
            "peppercorns", "olive oil", "vegetable oil", "canola oil (or peanut oil for frying)", "garlic cloves, minced", "yellow onion, diced", "shallots",
            "carrots, chopped", "celery stalks", "tomatoes, diced", "tomato paste", "red bell pepper", "jalapeño, seeded", "chicken breasts", "chicken broth",
            "beef stock", "ground beef", "bacon, chopped", "ham, cubed", "pork shoulder", "turkey bacon", "salmon fillets", "shrimp, peeled", "anchovies",
            "canned tuna", "crab meat", "oyster crackers", "soy sauce", "tamari", "Worcestershire sauce", "fish sauce", "rice vinegar", "red wine vinegar",
            "dry white wine", "a glass of sherry", "brandy", "dark rum", "beer", "vanilla extract", "baking soda", "baking powder", "cornstarch", "cornmeal",
            "rolled oats", "quinoa", "brown rice", "pasta", "egg noodles", "bread crumbs", "panko", "graham crackers", "peanut butter", "almond flour",
            "coconut milk", "coconut flour", "walnuts, chopped", "pecans", "almonds, sliced", "cashews", "pine nuts", "sesame seeds", "tahini", "chickpeas",
            "black beans, rinsed", "lentils", "green peas", "frozen corn", "eggplant, cubed", "zucchini", "potatoes, peeled", "sweet potatoes", "spinach",
            "kale", "lemon juice", "lime zest", "orange juice", "apples, sliced", "pineapple chunks", "bananas", "raisins", "dried apricots", "dates",
            "fresh basil", "dried oregano", "ground cumin", "chili powder", "paprika", "cinnamon", "nutmeg", "ground ginger", "fresh parsley", "thyme",
            "rosemary", "bay leaves", "mayonnaise", "dijon mustard", "ketchup", "barbecue sauce", "hot sauce", "salsa", "tortillas", "pita bread",
            "hamburger buns", "dark chocolate", "cocoa powder", "chocolate chips", "coffee", "espresso", "green tea", "gelatin", "marshmallows",
            "yeast", "water", "ice water", "warm water (110°F)", "crème fraîche", "½ cup sugar", "buckwheat groats", "barley", "rye flour", "couscous",
            "a wineglassful of water", "1 teacupful of milk", "sauerkraut", "dill pickles", "capers", "olives", "feta cheese", "goat cheese", "tofu",
            "tempeh", "seitan", "nutritional yeast", "lard", "suet", "fatback", "duck fat", "sausage meat", "pepperoni", "salami", "prosciutto",
        };

        private static readonly string[] Amounts = { "1", "2", "3", "1/2", "1/4", "3/4", "1 1/2", "4", "6", "½", "¼", "a", "one", "two", "8" };
        private static readonly string[] Units = { "cup", "cups", "tbsp", "tsp", "tablespoons", "teaspoon", "oz", "ounces", "lb", "pound", "g", "ml", "pinch of", "dash of", "can", "" };

        private static ulong Mix(ulong x)
        {
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb3fe1a85ec53UL;
            return x ^ (x >> 33);
        }

        private static string LineText(long recipeId, int line, int version)
        {
            var h = Mix((ulong)recipeId * 1_000_003UL + (ulong)line * 7919UL + (ulong)version * 104_729UL);
            var food = Foods[(int)(h % (ulong)Foods.Length)];
            var amount = Amounts[(int)((h >> 20) % (ulong)Amounts.Length)];
            var unit = Units[(int)((h >> 30) % (ulong)Units.Length)];
            var note = ((h >> 40) % 9) switch { 0 => ", divided", 1 => " (optional)", 2 => ", plus more for serving", _ => string.Empty };
            return (amount + " " + unit + " " + food + note).Replace("  ", " ");
        }

        private sealed class Catalog
        {
            public Catalog(int recipes, int lines)
            {
                Ids = new long[recipes];
                Counts = new int[recipes];
                var total = 0;
                for (var r = 0; r < recipes; r++)
                {
                    Ids[r] = 1000 + r + r / 7;
                    Counts[r] = 2 + (int)(Mix((ulong)r) % 10);
                    total += Counts[r];
                }
                for (var r = 0; total != lines; r = (r + 1) % recipes)
                {
                    if (total < lines && Counts[r] < 30) { Counts[r]++; total++; }
                    else if (total > lines && Counts[r] > 1) { Counts[r]--; total--; }
                }
                Lines = lines;
            }

            public long[] Ids { get; }

            public int[] Counts { get; }

            public int Lines { get; }

            public Task<List<DietLineRules.Line>> ReadAsync(long after, long upTo)
            {
                var result = new List<DietLineRules.Line>();
                var start = Array.BinarySearch(Ids, after + 1);
                if (start < 0) start = ~start;
                for (var r = start; r < Ids.Length && Ids[r] <= upTo; r++)
                    for (var l = 0; l < Counts[r]; l++)
                        result.Add(new DietLineRules.Line(Ids[r], 10_000 + (long)(Mix((ulong)(Ids[r] * 31 + l)) % 400), l, LineText(Ids[r], l, 0)));
                return Task.FromResult(result);
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task Every_type_reads_a_production_sized_catalog_within_minutes_as_the_matcher_does()
        {
            var catalog = new Catalog(ProductionRecipes, ProductionLines);
            var criteria = DefaultCriteria();
            var matcher = new DietLineRules.Matcher(DietLineRules.Rules(criteria));

            var clock = Stopwatch.StartNew();
            var readings = new System.Collections.Concurrent.ConcurrentDictionary<long, string[]>();
            var lines = 0;
            for (var after = 0L; after < catalog.Ids[^1]; after += DietGateStoreBuilder.LineWindow)
            {
                var groups = (await catalog.ReadAsync(after, Math.Min(after + DietGateStoreBuilder.LineWindow, catalog.Ids[^1]))).GroupBy(l => l.RecipeId).Select(g => g.ToList()).ToList();
                lines += groups.Sum(g => g.Count);
                await Task.Run(() => Parallel.ForEach(groups, recipe => readings[recipe[0].RecipeId] = matcher.Read(recipe, true).Rules));
            }
            clock.Stop();
            _output.WriteLine($"read {lines:N0} lines of {readings.Count:N0} recipes in {clock.Elapsed.TotalSeconds:F1} s");
            lines.Should().Be(ProductionLines);
            readings.Count.Should().Be(ProductionRecipes);
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(5), "a whole cycle reads every line within minutes");

            var sample = Enumerable.Range(0, 300).Select(i => catalog.Ids[(int)(Mix((ulong)i + 77) % (ulong)catalog.Ids.Length)]).Distinct().ToList();
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId).Select(g => g.ToList()))
            {
                var patterns = DietCriteriaMatcher.LinePatterns(type);
                var keys = patterns.Select(DietLineRules.KeyOf).OfType<string>().ToHashSet(StringComparer.Ordinal);
                foreach (var id in sample)
                {
                    var texts = (await catalog.ReadAsync(id - 1, id)).Select(l => l.RawLine).ToList();
                    readings[id].Any(keys.Contains).Should().Be(texts.Any(l => patterns.Any(c => DietCriteriaMatcher.LineNames(c, l))), $"{type[0].TypeName} reads recipe {id} as the matcher does");
                }
            }
        }
    }
}
