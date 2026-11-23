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
    /// The diet gate's line index (staging checkpoint 4, P1): no household set probes the line table. On generated
    /// data of the production copy's size and shape (843,636 live lines over 122,580 recipes) every default type is
    /// read within minutes and each answers in under a second; before the warm every recipe is held, a recipe made
    /// since the last pass is held until the next reads it, and the reading is the matcher's own, line by line and on
    /// the golden diet rows (<see cref="AssertIndexAgreesAsync"/>, run by the golden diet tests).
    /// </summary>
    public class DietLineIndexTests
    {
        private const long HouseholdId = 7, PersonId = 70, CeliacRef = 60800, SulfiteRef = 60801, NoAlcoholRef = 60913, AlcoholNutrient = 5038, Gram = 1;
        private const int ProductionLines = 843_636, ProductionRecipes = 122_580;

        private readonly ITestOutputHelper _output;

        public DietLineIndexTests(ITestOutputHelper output) => _output = output;

        /// <summary>Asserts that a resolver reading the line index after a warm answers exactly as the in-line matcher does, for the household and for the person's severe set.</summary>
        internal static async Task AssertIndexAgreesAsync(ApplicationDbContext db, long householdId, long personId)
        {
            var cache = new DietGateCache(refreshInBackground: false);
            var index = new DietLineIndex();
            await index.WarmAsync(db, cache);
            var inline = new HouseholdRestrictionResolver(db);
            var indexed = new HouseholdRestrictionResolver(db, cache, index);
            foreach (var (expected, actual) in new[]
                     {
                         (await inline.ResolveAsync(householdId), await indexed.ResolveAsync(householdId)),
                         (await inline.ResolveSevereForPersonAsync(personId), await indexed.ResolveSevereForPersonAsync(personId)),
                     })
            {
                actual.Pending.Should().BeFalse();
                actual.IngredientIds.Should().BeEquivalentTo(expected.IngredientIds, "the index reads the foods as the matcher does");
                actual.RecipeIds.Should().BeEquivalentTo(expected.RecipeIds, "the index reads every line as the matcher does");
                actual.HasSevere.Should().Be(expected.HasSevere);
            }
        }

        private static ApplicationDbContext NewContext(string? name = null) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private sealed record Kitchen(long Flour, long Rice, long Water, long Bread, long Pilaf, long Toast);

        private static async Task<Kitchen> SeedAsync(ApplicationDbContext db, string type = "Celiac Disease", long typeId = CeliacRef)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId, Name = type });
            var flour = new IngredientEntity { Name = "Wheat Flour", CurationStatusId = (long)CurationStatusEnum.Curated };
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            var water = new IngredientEntity { Name = "Water", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(flour, rice, water);
            await db.SaveChangesAsync();
            var bread = Recipe("Bread", (flour.Id, "3 cups wheat flour"), (water.Id, "1 cup water"));
            var pilaf = Recipe("Pilaf", (rice.Id, "1 cup rice"), (water.Id, "2 cups water"));
            var toast = Recipe("Toast", (rice.Id, "2 slices of bread, toasted"));
            db.Recipes.AddRange(bread, pilaf, toast);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return new Kitchen(flour.Id, rice.Id, water.Id, bread.Id, pilaf.Id, toast.Id);
        }

        [Fact]
        public async Task Before_the_warm_every_recipe_is_held_and_no_probe_is_computed_in_line()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var cache = new DietGateCache();
            var index = new DietLineIndex();
            var resolver = new HouseholdRestrictionResolver(db, cache, index);

            var clock = Stopwatch.StartNew();
            var cold = await resolver.ResolveAsync(HouseholdId);
            var severe = await resolver.ResolveSevereForPersonAsync(PersonId);

            cold.RecipeIds.Should().Contain(new[] { k.Bread, k.Pilaf, k.Toast }, "a recipe not yet read is held, never shown as fine");
            cold.Pending.Should().BeTrue();
            cold.ReadUpTo.Should().Be(0);
            severe.RecipeIds.Should().Contain(new[] { k.Bread, k.Pilaf, k.Toast });
            severe.HasSevere.Should().BeTrue();
            index.Current(db).Should().BeNull("no household set reads the lines; only the warm does");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            await index.WarmAsync(db, cache);
            var warm = await resolver.ResolveAsync(HouseholdId);
            warm.Pending.Should().BeFalse();
            warm.IngredientIds.Should().Contain(k.Flour);
            warm.RecipeIds.Should().Contain(k.Toast, "its line names bread on a food no pattern names").And.NotContain(k.Pilaf);
            await AssertIndexAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_recipe_made_after_a_pass_is_held_until_the_next_pass_reads_it_and_a_changed_line_is_read_again()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var cache = new DietGateCache(refreshInBackground: false);
            var index = new DietLineIndex();
            await index.WarmAsync(db, cache);
            var resolver = new HouseholdRestrictionResolver(db, cache, index);

            var risotto = Recipe("Risotto", (k.Rice, "1 cup arborio rice"));
            db.Recipes.Add(risotto);
            var pilaf = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == k.Pilaf && ri.SortOrder == 0);
            pilaf.RawLine = "1 cup bulgur wheat";
            await db.SaveChangesAsync();

            var before = await resolver.ResolveAsync(HouseholdId);
            before.RecipeIds.Should().Contain(risotto.Id, "made since the pass, so not yet read");
            before.ReadUpTo.Should().BeLessThan(risotto.Id);

            var pass = await index.RefreshAsync(db);
            pass.Read.Should().Be(2, "only the new recipe and the changed one are read again");
            var after = await resolver.ResolveAsync(HouseholdId);
            after.RecipeIds.Should().NotContain(risotto.Id).And.Contain(k.Pilaf);
            await AssertIndexAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_rule_the_index_has_not_read_holds_every_recipe_and_asks_for_a_pass()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var cache = new DietGateCache(refreshInBackground: false);
            var index = new DietLineIndex();
            await index.WarmAsync(db, cache);
            var resolver = new HouseholdRestrictionResolver(db, cache, index);
            index.TakeRequest(db);

            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = CeliacRef, IngredientPattern = "%arborio%", Severity = 5, Notes = "admin", CreatedDate = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await DietCriteriaMatcher.WarmFoodsAsync(db, cache);

            var held = await resolver.ResolveAsync(HouseholdId);
            held.RecipeIds.Should().Contain(new[] { k.Bread, k.Pilaf, k.Toast });
            held.ReadUpTo.Should().Be(0);
            index.TakeRequest(db).Should().BeTrue("the warm is asked for a pass that reads the new rule");

            var pass = await index.RefreshAsync(db);
            pass.NewRules.Should().Be(1);
            pass.Read.Should().Be(0, "no line changed, so each recipe is read for the new rule only");
            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().NotContain(k.Pilaf);
            await AssertIndexAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_peek_never_computes_in_line_serves_a_stale_value_while_it_refreshes_and_nothing_after_a_failed_refresh()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var db = new ApplicationDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var cache = new DietGateCache();
            var fail = false;
            var calls = 0;
            Task<string> Compute(ApplicationDbContext _)
            {
                var call = Interlocked.Increment(ref calls);
                return Volatile.Read(ref fail) ? throw new TimeoutException("probe timed out") : Task.FromResult("v" + call);
            }
            async Task<string?> UntilAsync(Func<string?, bool> done)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                string? value;
                while (!done(value = await cache.PeekAsync(db, "foods", Compute)) && DateTime.UtcNow < deadline) await Task.Delay(20);
                return value;
            }

            (await cache.PeekAsync(db, "foods", Compute)).Should().BeNull("the first read starts the compute apart and does not wait for it");
            (await UntilAsync(v => v != null)).Should().Be("v1");
            cache.Invalidate();
            Volatile.Write(ref fail, true);
            (await cache.PeekAsync(db, "foods", Compute)).Should().Be("v1", "a stale value is served while it refreshes");
            (await UntilAsync(v => v == null)).Should().BeNull("a value whose refresh failed is not served");
            Volatile.Write(ref fail, false);
            (await UntilAsync(v => v != null)).Should().StartWith("v");
        }

        private sealed record Cellar(long Rice, long Custard, long Pudding, long Trifle);

        private static async Task<Cellar> SeedNoAlcoholAsync(ApplicationDbContext db)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" });
            db.Nutrients.Add(new NutrientEntity { Id = AlcoholNutrient, Name = "Alcohol", DefaultMeasurementId = Gram });
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(rice);
            await db.SaveChangesAsync();
            var custard = Recipe("Custard", (rice.Id, "1 cup rice"));
            var pudding = Recipe("Pudding", (rice.Id, "2 cups rice"));
            db.Recipes.AddRange(custard, pudding);
            await db.SaveChangesAsync();
            var trifle = Recipe("Trifle", (rice.Id, "1 glass of sherry"));
            db.Recipes.Add(trifle);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return new Cellar(rice.Id, custard.Id, pudding.Id, trifle.Id);
        }

        [Fact]
        public async Task A_retained_alcohol_count_a_request_refreshed_on_the_last_reading_is_counted_again_once_a_pass_reads_the_changed_line()
        {
            using var db = NewContext();
            var c = await SeedNoAlcoholAsync(db);
            var cache = new DietGateCache();
            var index = new DietLineIndex();
            await index.WarmAsync(db, cache);
            var resolver = new HouseholdRestrictionResolver(db, cache, index);
            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().Contain(c.Trifle).And.NotContain(c.Custard);

            var line = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == c.Custard);
            line.RawLine = "1 cup brandy";
            await db.SaveChangesAsync();
            cache.Invalidate();
            var computed = cache.Computations;
            await resolver.ResolveAsync(HouseholdId);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (cache.Computations < computed + 2 && DateTime.UtcNow < deadline) await Task.Delay(20);

            await index.WarmAsync(db, cache, everyType: false);

            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().Contain(c.Custard,
                "the count a request refreshed before the pass read the brandy line is not the pass's count (review CK9S-1)");
        }

        [Fact]
        public async Task A_retained_alcohol_count_made_on_a_part_read_reading_holds_every_recipe_above_what_it_read()
        {
            using var db = NewContext();
            var c = await SeedNoAlcoholAsync(db);
            var cache = new DietGateCache();
            var index = new DietLineIndex();
            var key = DietGateCache.DatabaseKey(db);
            Task<List<DietLineIndex.Line>> Lines(long after, long upTo, CancellationToken ct) => db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RecipeId > after && ri.RecipeId <= upTo)
                .Select(ri => new DietLineIndex.Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                .ToListAsync(ct);
            var rules = await DietLineIndex.RulesAsync(db);
            await DietCriteriaMatcher.WarmFoodsAsync(db, cache);
            await index.RefreshAsync(key, rules, c.Pudding, Lines);
            await DietCriteriaMatcher.WarmRetainedAsync(db, cache, index);

            await index.RefreshAsync(key, rules, c.Trifle, Lines);
            var set = await new HouseholdRestrictionResolver(db, cache, index).ResolveAsync(HouseholdId);

            set.RecipeIds.Should().Contain(c.Trifle, "the count was made before the pass read the trifle's sherry, so it cannot clear it (review CK9S-1)");
            set.RecipeIds.Should().NotContain(c.Custard);
        }

        [Fact]
        public async Task The_count_per_type_reads_what_the_warm_keeps_and_shows_a_type_not_warm_as_pending()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var cache = new DietGateCache(refreshInBackground: false);
            var index = new DietLineIndex();

            var cold = await DietGateCounts.ReadAsync(db, cache, index);
            cold.Types.Should().Contain(t => t.TypeId == CeliacRef && t.Pending);
            cold.IndexRecipes.Should().Be(0);

            await index.WarmAsync(db, cache);
            var warm = await DietGateCounts.ReadAsync(db, cache, index);
            var celiac = warm.Types.Single(t => t.TypeId == CeliacRef && t.Reading == "all");
            celiac.Pending.Should().BeFalse();
            celiac.Name.Should().Be("Celiac Disease");
            celiac.Foods.Should().BeGreaterThanOrEqualTo(1);
            celiac.RecipesByFood.Should().Be(1, "only the bread has a line on wheat flour");
            celiac.RecipesHeld.Should().Be(2, "the bread by its food and the toast by its line's own text");
            warm.Types.Should().Contain(t => t.TypeId == CeliacRef && t.Reading == "severe");
            warm.IndexRecipes.Should().Be(3);
            warm.IndexLines.Should().Be(5);
            warm.IndexReadUpTo.Should().BeGreaterThanOrEqualTo(k.Toast);
            warm.Recipes.Should().Be(3);
        }

        [Fact]
        public async Task The_count_per_type_counts_only_recipes_that_are_not_deleted_as_the_sql_count_does()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var gone = Recipe("Old Loaf", (k.Flour, "2 cups wheat flour"), (k.Rice, "a slice of bread"));
            db.Recipes.Add(gone);
            await db.SaveChangesAsync();
            gone.IsDeleted = true;
            await db.SaveChangesAsync();
            var cache = new DietGateCache(refreshInBackground: false);
            var index = new DietLineIndex();
            await index.WarmAsync(db, cache);

            var counts = await DietGateCounts.ReadAsync(db, cache, index);

            var celiac = counts.Types.Single(t => t.TypeId == CeliacRef && t.Reading == "all");
            celiac.RecipesByFood.Should().Be(1, "a deleted recipe is no recipe the gate offers or holds (review CK9S-4)");
            celiac.RecipesByText.Should().Be(2, "the bread and the toast by their lines' own text, not the deleted loaf");
            celiac.RecipesHeld.Should().Be(2);
            counts.Recipes.Should().Be(3, "the live recipes, the base a share of held recipes is read against");
            counts.IndexRecipes.Should().Be(4, "the index itself reads every live line, whatever its recipe");
        }

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

            public HashSet<long> Changed { get; } = new();

            public Task<List<DietLineIndex.Line>> ReadAsync(long after, long upTo)
            {
                var result = new List<DietLineIndex.Line>();
                var start = Array.BinarySearch(Ids, after + 1);
                if (start < 0) start = ~start;
                for (var r = start; r < Ids.Length && Ids[r] <= upTo; r++)
                    for (var l = 0; l < Counts[r]; l++)
                        result.Add(new DietLineIndex.Line(Ids[r], 10_000 + (long)(Mix((ulong)(Ids[r] * 31 + l)) % 400), l, LineText(Ids[r], l, Changed.Contains(Ids[r]) ? 1 : 0)));
                return Task.FromResult(result);
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task Every_type_reads_a_production_sized_catalog_within_minutes_and_answers_in_under_a_second()
        {
            var catalog = new Catalog(ProductionRecipes, ProductionLines);
            var criteria = DefaultCriteria();
            var rules = DietLineIndex.Rules(criteria);
            var index = new DietLineIndex();
            const string key = "generated";

            var first = await index.RefreshAsync(key, rules, catalog.Ids[^1], (a, u, _) => catalog.ReadAsync(a, u));
            _output.WriteLine($"first pass: {first.Lines:N0} lines, {first.Recipes:N0} recipes, {first.Rules} rules, {first.Took.TotalSeconds:F1} s");
            first.Lines.Should().Be(ProductionLines);
            first.Recipes.Should().Be(ProductionRecipes);
            first.Took.Should().BeLessThan(TimeSpan.FromMinutes(5), "every type must be warm within minutes");

            var snapshot = index.Current(key)!;
            var slowest = TimeSpan.Zero;
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId))
            {
                foreach (var list in new[] { type.ToList(), type.Where(c => c.Severity >= 4).ToList() }.Where(l => l.Count > 0))
                {
                    var clock = Stopwatch.StartNew();
                    var view = snapshot.ViewOf(list);
                    var aliasLines = snapshot.RecipesOn(new long[] { 10_001, 10_002, 10_003 }).Where(view.Named.Contains).Count();
                    clock.Stop();
                    view.Missing.Should().BeEmpty();
                    view.ReadUpTo.Should().Be(catalog.Ids[^1]);
                    if (clock.Elapsed > slowest) slowest = clock.Elapsed;
                    clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), $"{type.First().TypeName} answers from memory");
                    aliasLines.Should().BeGreaterThanOrEqualTo(0);
                }
            }
            _output.WriteLine($"slowest type read: {slowest.TotalMilliseconds:F0} ms");
            snapshot.ViewOf(criteria.Where(c => c.TypeName == "Celiac Disease").ToList()).LineRecipes.Count.Should().BeGreaterThan(ProductionRecipes / 4);

            var sample = Enumerable.Range(0, 300).Select(i => catalog.Ids[(int)(Mix((ulong)i + 77) % (ulong)catalog.Ids.Length)]).Distinct().ToList();
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId).Select(g => g.ToList()))
            {
                var view = snapshot.ViewOf(type);
                var patterns = DietCriteriaMatcher.LinePatterns(type);
                foreach (var id in sample)
                {
                    var lines = (await catalog.ReadAsync(id - 1, id)).Select(l => l.RawLine).ToList();
                    view.LineRecipes.Contains(id).Should().Be(lines.Any(l => patterns.Any(c => DietCriteriaMatcher.LineNames(c, l))), $"{type[0].TypeName} reads recipe {id} as the matcher does");
                }
            }

            foreach (var id in catalog.Ids.Where((_, i) => i % 100 == 0)) catalog.Changed.Add(id);
            var second = await index.RefreshAsync(key, rules, catalog.Ids[^1], (a, u, _) => catalog.ReadAsync(a, u));
            _output.WriteLine($"second pass, {catalog.Changed.Count:N0} recipes changed: read {second.Read:N0} in {second.Took.TotalSeconds:F1} s");
            second.Read.Should().Be(catalog.Changed.Count, "a recipe whose lines did not change keeps its reading");
        }
    }
}
