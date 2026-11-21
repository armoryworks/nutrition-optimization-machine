using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The diet gate by data (alcohol design A9): No Alcohol, Halal and Whole30 read alcohol from the
    /// food's stored alcohol (nutrient 5038) and, for a food with no alcohol figure, from the alcohol
    /// sense of its name through word-bounded patterns. Bread Crumbs, Ginger Root, Crumbled Feta and
    /// the wine vinegars no longer match; Vanilla Extract is left to a note (owner answer 6); a wine,
    /// a spirit and an unlinked "glass of sherry" still do.
    /// </summary>
    public class DietGateByDataTests
    {
        private const long HouseholdId = 7, PersonId = 70, NoAlcoholRef = 60913, HalalRef = 60914, CeliacRef = 60800, Alcohol = 5038, Gram = 1;
        private const long Curated = (long)CurationStatusEnum.Curated, Pending = (long)CurationStatusEnum.PendingCuration;

        private static readonly string[] OldAlcoholPatterns = { "%wine%", "%beer%", "%vodka%", "%rum%", "%whisk%", "%bourbon%", "%brandy%", "%liqueur%", "%sake%", "%tequila%", "%gin%", "%sherry%", "%vermouth%", "%cider%" };

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private sealed record Foods(Dictionary<string, long> Ids);

        private static async Task<Foods> SeedAsync(ApplicationDbContext db, bool withNutrient = true)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" },
                new ReferenceEntity { Id = HalalRef, Name = "Halal" },
                new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" });
            if (withNutrient) db.Nutrients.Add(new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = Gram });
            var foods = new (string Name, long Status, decimal? Alcohol, int? Category)[]
            {
                ("Bread Crumbs", Curated, 0m, 18),
                ("Ginger Root", Curated, null, 11),
                ("Crumbled Feta", Curated, null, 1),
                ("Red Wine Vinegar", Curated, 0m, 2),
                ("Apple Cider Vinegar", Curated, 0m, 2),
                ("Vanilla Extract", Curated, 34.4m, 2),
                ("Dry White Wine", Curated, 10.3m, 14),
                ("Coffee Cream Liqueur", Curated, 10.9m, 14),
                ("Beer Salami", Curated, 0m, 7),
                ("glass of sherry", Pending, null, null),
                ("brandy", Pending, null, null),
                ("rum extract", Pending, null, null),
                ("oyster liquor", Pending, null, null),
                ("ginger beer", Pending, null, null),
                ("sweet cider", Pending, null, null),
                ("Gingerbread", Curated, null, 18),
            };
            var ids = new Dictionary<string, long>();
            foreach (var (name, status, alcohol, category) in foods)
            {
                var food = new IngredientEntity { Name = name, CurationStatusId = status, FdcCategoryId = category };
                if (alcohol is { } grams) food.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = grams, MeasurementId = Gram });
                db.Ingredients.Add(food);
                await db.SaveChangesAsync();
                ids[name] = food.Id;
            }
            return new Foods(ids);
        }

        private static async Task<RestrictedIngredientSet> ResolveAsync(ApplicationDbContext db, long typeId, string name)
        {
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = name, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
        }

        [Fact]
        public async Task No_alcohol_flags_drinks_by_data_and_by_the_sense_of_unlinked_names_and_nothing_else()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            set.IngredientIds.Should().Contain(new[] { foods.Ids["glass of sherry"], foods.Ids["brandy"] });
            set.IngredientIds.Should().NotContain(new[] { foods.Ids["Dry White Wine"], foods.Ids["Coffee Cream Liqueur"] },
                "No Alcohol caps retained alcohol per serving (owner answer 6), so a food with an alcohol figure is left to each recipe's count");
            set.IngredientIds.Should().NotContain(new[]
            {
                foods.Ids["Bread Crumbs"], foods.Ids["Ginger Root"], foods.Ids["Crumbled Feta"], foods.Ids["Red Wine Vinegar"], foods.Ids["Apple Cider Vinegar"],
                foods.Ids["Vanilla Extract"], foods.Ids["Beer Salami"], foods.Ids["rum extract"], foods.Ids["oyster liquor"], foods.Ids["ginger beer"],
                foods.Ids["sweet cider"], foods.Ids["Gingerbread"],
            });
            set.HasSevere.Should().BeFalse("No Alcohol is a diet, severity 3");
        }

        [Fact]
        public async Task Halal_keeps_its_pork_patterns_and_reads_alcohol_by_data()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            var bacon = new IngredientEntity { Name = "Bacon", CurationStatusId = Curated };
            db.Ingredients.Add(bacon);
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, HalalRef, "Halal");

            set.IngredientIds.Should().Contain(bacon.Id).And.Contain(foods.Ids["brandy"]);
            set.IngredientIds.Should().NotContain(foods.Ids["Dry White Wine"], "Halal caps retained alcohol per serving (owner answer 6)");
            set.IngredientIds.Should().NotContain(foods.Ids["Bread Crumbs"]).And.NotContain(foods.Ids["Red Wine Vinegar"]).And.NotContain(foods.Ids["Ginger Root"]);
        }

        [Fact]
        public async Task Without_an_alcohol_nutrient_the_word_patterns_alone_still_flag_every_drink_name()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db, withNutrient: false);

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            set.IngredientIds.Should().Contain(new[] { foods.Ids["Dry White Wine"], foods.Ids["Coffee Cream Liqueur"], foods.Ids["glass of sherry"], foods.Ids["brandy"] });
            set.IngredientIds.Should().NotContain(new[] { foods.Ids["Bread Crumbs"], foods.Ids["Ginger Root"], foods.Ids["Red Wine Vinegar"], foods.Ids["Vanilla Extract"] });
        }

        [Fact]
        public async Task Ensure_replaces_the_untouched_alcohol_name_patterns_and_leaves_an_edited_type_alone()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var now = DateTime.UtcNow;
            foreach (var pattern in OldAlcoholPatterns)
                db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = NoAlcoholRef, IngredientPattern = pattern, Severity = 3, Notes = DefaultRestrictionCriteria.DefaultNote, CreatedDate = now });
            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = HalalRef, IngredientPattern = "%rum%", Severity = 3, Notes = "admin" });
            await db.SaveChangesAsync();

            await DefaultRestrictionCriteria.EnsureAsync(db);
            var second = await DefaultRestrictionCriteria.EnsureAsync(db);

            var noAlcohol = await db.Set<RestrictionCriterionEntity>().Where(c => c.RestrictionTypeId == NoAlcoholRef).ToListAsync();
            noAlcohol.Select(c => c.IngredientPattern).Should().NotContain(OldAlcoholPatterns).And.Contain(@"\mrum\M").And.Contain(@"\mgin\M");
            noAlcohol.Should().ContainSingle(c => c.NutrientId == Alcohol && c.IngredientPattern == null && c.MaxAmountPerServing == 0.5m);
            second.Should().Be(0);
            (await db.Set<RestrictionCriterionEntity>().CountAsync(c => c.RestrictionTypeId == HalalRef)).Should().Be(1, "an admin-edited type is left alone");
        }

        [Fact]
        public async Task A_recipe_with_a_drink_line_is_flagged_for_no_alcohol_and_a_recipe_with_only_crumbs_ginger_and_vinegar_is_not()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            var plain = Recipe("Breaded fish", foods.Ids["Bread Crumbs"], foods.Ids["Ginger Root"], foods.Ids["Red Wine Vinegar"], foods.Ids["Vanilla Extract"]);
            var trifle = Recipe("Trifle", foods.Ids["glass of sherry"], foods.Ids["Bread Crumbs"]);
            var fish = Recipe("Fish in white wine", foods.Ids["Dry White Wine"]);
            db.Recipes.AddRange(plain, trifle, fish);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

            (await service.GetDietMatchesAsync(plain.Id, PersonId)).Should().BeEmpty();
            (await service.GetDietMatchesAsync(trifle.Id, PersonId)).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { RestrictionName = "No Alcohol", IngredientName = "glass of sherry", Severity = (int?)3, Notes = (string?)null });
            (await service.GetDietMatchesAsync(fish.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("Dry White Wine");
        }

        [Fact]
        public async Task A_drink_line_linked_to_a_food_that_is_not_a_drink_keeps_its_recipe_flagged_and_a_vinegar_line_on_the_same_food_does_not()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            var sugar = new IngredientEntity { Name = "Sugar", CurationStatusId = Curated };
            db.Ingredients.Add(sugar);
            await db.SaveChangesAsync();
            var trifle = Recipe("Trifle", (foods.Ids["Red Wine Vinegar"], "1 glass of sherry"), (foods.Ids["Bread Crumbs"], "1 cup bread crumbs"));
            var salad = Recipe("Salad", (foods.Ids["Red Wine Vinegar"], "2 tbsp red wine vinegar"), (foods.Ids["Apple Cider Vinegar"], "1 tbsp apple cider vinegar"));
            var wassail = Recipe("Wassail", (foods.Ids["Apple Cider Vinegar"], "2 cups cider"));
            var syllabub = Recipe("Syllabub", (sugar.Id, "1/2 pint of Madeira"));
            db.Recipes.AddRange(trifle, salad, wassail, syllabub);
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

            set.RecipeIds.Should().Contain(new[] { trifle.Id, wassail.Id }).And.NotContain(salad.Id);
            set.IngredientIds.Should().NotContain(new[] { foods.Ids["Red Wine Vinegar"], foods.Ids["Apple Cider Vinegar"] }, "the vinegar itself stays cleared");
            (await service.GetDietMatchesAsync(trifle.Id, PersonId)).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { RestrictionName = "No Alcohol", IngredientId = foods.Ids["Red Wine Vinegar"], IngredientName = "1 glass of sherry", Severity = (int?)3 });
            (await service.GetDietMatchesAsync(syllabub.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("1/2 pint of Madeira");
            (await service.GetDietMatchesAsync(salad.Id, PersonId)).Should().BeEmpty();
            (await service.GetSevereDietConflictsAsync(new[] { trifle.Id }, PersonId)).Should().BeEmpty("No Alcohol is severity 3");
        }

        [Fact]
        public async Task A_household_with_no_alcohol_is_never_offered_a_recipe_whose_drink_line_is_linked_to_a_vinegar()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            const long Approved = 990001;
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = Approved, Name = "Approved" });
            db.Persons.Add(new PersonEntity { Id = PersonId, Name = "Cook" });
            var trifle = Recipe("Trifle", (foods.Ids["Red Wine Vinegar"], "1 glass of sherry"));
            var salad = Recipe("Salad", (foods.Ids["Red Wine Vinegar"], "2 tbsp red wine vinegar"));
            foreach (var recipe in new[] { trifle, salad })
            {
                recipe.Visibility = RecipeVisibilityEnum.Public;
                recipe.CurationStatusId = Approved;
            }
            db.Recipes.AddRange(trifle, salad);
            await db.SaveChangesAsync();
            await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            var offered = await new RecipeSearchOrchestrationService(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeSearchOrchestrationService>.Instance)
                .GetRandomRecipesAsync(10, HouseholdId);

            offered.Results.Select(r => r.Name).Should().Equal("Salad");
        }

        [Fact]
        public void A_word_pattern_is_matched_on_word_bounds()
        {
            DietCriteriaMatcher.IsWordPattern(@"\mrum\M").Should().BeTrue();
            DietCriteriaMatcher.IsWordPattern("%rum%").Should().BeFalse();
            DietCriteriaMatcher.WordRegex(@"\mrum\M").IsMatch("Bread Crumbs").Should().BeFalse();
            DietCriteriaMatcher.WordRegex(@"\mrum\M").IsMatch("Dark Rum").Should().BeTrue();
            DietCriteriaMatcher.WordRegex(@"\mgin\M").IsMatch("Ginger Root").Should().BeFalse();
            DietCriteriaMatcher.WordRegex(@"\mwhiske?y\M").IsMatch("Scotch whisky").Should().BeTrue();
        }

        [Fact]
        public void The_diet_gate_queries_translate_to_postgres_word_matches_and_a_nutrient_filter()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var scope = new List<long> { 1, 2 };

            DietCriteriaMatcher.WordNameRows(db, AlcoholLineContext.DrinkNamePattern, scope).ToQueryString().Should().Contain("\"Name\" ~*").And.NotContain("\"AliasName\"");
            DietCriteriaMatcher.WordAliasRows(db, @"\mrum\M", null).ToQueryString().Should().Contain("\"AliasName\" ~*");
            DietCriteriaMatcher.NutrientRows(db, Alcohol, scope).ToQueryString().Should().Contain("\"Amount\" >").And.Contain("\"FdcCategoryId\"");
            db.Set<RestrictionCriterionEntity>().Where(c => c.IngredientPattern != null && !c.IngredientPattern.StartsWith(DietCriteriaMatcher.WordMarker)).ToQueryString()
                .Should().Contain("LIKE");
        }

        [Fact]
        public void The_drink_line_probe_is_one_word_match_over_the_lines_with_no_list_of_foods_and_no_join()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

            var sql = DietCriteriaMatcher.DrinkLineRows(db).ToQueryString();

            sql.Should().Contain("\"RawLine\" ~*").And.Contain("FROM recipe.\"RecipeIngredient\"");
            sql.Should().NotContain("ANY").And.NotContain("JOIN").And.NotContain("\"AliasName\"").And.NotContain("recipe.\"Ingredient\" AS");
            Regex.Matches(sql, "~\\*").Count.Should().Be(1);
        }

        [Fact]
        public async Task A_food_counts_by_its_own_name_and_never_by_an_alias_that_is_a_recipe_line_while_each_drink_line_decides_for_its_recipe()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            var aliased = new (string Name, string[] Aliases)[]
            {
                ("Sugar", new[] { "little sugar and brandy or raisin wine", "sugar, and 1 small cup of wine" }),
                ("Milk", new[] { "apple cider", "and a-half of milk or beer", "glass of the best port wine or a glass of new unskimmed milk" }),
                ("Beef Broth", new[] { "beer" }),
                ("Dates", new[] { "drachms of roasted date stones, soaked in wine" }),
            };
            var ids = new Dictionary<string, long>();
            foreach (var (name, aliases) in aliased)
            {
                var food = new IngredientEntity { Name = name, CurationStatusId = Curated };
                foreach (var alias in aliases) food.Aliases.Add(new IngredientAliasEntity { AliasName = alias });
                db.Ingredients.Add(food);
                await db.SaveChangesAsync();
                ids[name] = food.Id;
            }
            var vinegar = foods.Ids["Red Wine Vinegar"];
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = vinegar, AliasName = "Shaoxing rice wine, dry sherry or sake" },
                new IngredientAliasEntity { IngredientId = vinegar, AliasName = "swish of sherry, red wine vinegar, or lemon juice" });
            var custard = Recipe("Vanilla custard", (ids["Milk"], "2 cups milk"), (ids["Sugar"], "1/2 cup sugar"), (foods.Ids["Vanilla Extract"], "1 tsp vanilla extract"));
            var salad = Recipe("Salad", (vinegar, "2 tbsp red wine vinegar"));
            var slaw = Recipe("Slaw", (foods.Ids["Apple Cider Vinegar"], "apple cider vinegar or lemon juice"), (ids["Sugar"], "2 tbsp sugar"));
            var punch = Recipe("Punch", (ids["Sugar"], "cola or ginger ale"));
            var stew = Recipe("Stew", (ids["Beef Broth"], "2 cups beef broth"), (ids["Dates"], "4 dates"));
            var pudding = Recipe("Plum pudding", (ids["Sugar"], "a little sugar and brandy"));
            var trifle = Recipe("Trifle", (vinegar, "two glasses of sherry"));
            var carbonnade = Recipe("Carbonnade", (ids["Beef Broth"], "1 pint of beer"));
            db.Recipes.AddRange(custard, salad, slaw, punch, stew, pudding, trifle, carbonnade);
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

            set.IngredientIds.Should().NotContain(new[] { ids["Sugar"], ids["Milk"], ids["Beef Broth"], ids["Dates"], vinegar }, "an alias that is a recipe line decides nothing for its food");
            set.RecipeIds.Should().Contain(new[] { pudding.Id, trifle.Id, carbonnade.Id }, "each drink line keeps its recipe out of the plans");
            set.RecipeIds.Should().NotContain(new[] { custard.Id, salad.Id, slaw.Id, punch.Id, stew.Id });
            foreach (var clear in new[] { custard, salad, slaw, punch, stew })
                (await service.GetDietMatchesAsync(clear.Id, PersonId)).Should().BeEmpty(clear.Name + " has no drink line");
            foreach (var flagged in new[] { pudding, trifle, carbonnade })
                (await service.GetDietMatchesAsync(flagged.Id, PersonId)).Should().ContainSingle(flagged.Name + " has one drink line");
        }

        [Fact]
        public async Task A_drink_line_on_a_food_with_no_drink_word_in_its_name_or_aliases_still_keeps_its_recipe_out_of_the_plans()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            var sugar = new IngredientEntity { Name = "Sugar", CurationStatusId = Curated };
            db.Ingredients.Add(sugar);
            await db.SaveChangesAsync();
            var syllabub = Recipe("Syllabub", (sugar.Id, "a glass of sherry"), (foods.Ids["Bread Crumbs"], "1 cup crumbs"));
            db.Recipes.Add(syllabub);
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            set.RecipeIds.Should().Contain(syllabub.Id);
            set.IngredientIds.Should().NotContain(sugar.Id);
        }

        [Fact]
        public async Task The_household_probes_run_once_and_again_only_after_a_write_or_when_the_value_is_old()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            var trifle = Recipe("Trifle", (foods.Ids["Red Wine Vinegar"], "1 glass of sherry"));
            db.Recipes.Add(trifle);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var now = new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc);
            var cache = new DietGateCache(TimeSpan.FromMinutes(15), () => now, refreshInBackground: false);
            var resolver = new HouseholdRestrictionResolver(db, cache);

            var warmed = await DietCriteriaMatcher.WarmAsync(db, cache);
            var first = await resolver.ResolveAsync(HouseholdId);
            var probes = cache.Computations;
            var second = await resolver.ResolveAsync(HouseholdId);
            await resolver.ResolveSevereForPersonAsync(PersonId);

            warmed.Should().Be(3, "the start-up warm computes the foods of No Alcohol and of Halal, the two types with data criteria, and the drink lines");
            probes.Should().Be(warmed, "a household set after the warm reads the kept probes");
            cache.Computations.Should().Be(probes, "a second household set reads the kept probes");
            second.RecipeIds.Should().BeEquivalentTo(first.RecipeIds).And.Contain(trifle.Id);

            var wassail = Recipe("Wassail", (foods.Ids["Apple Cider Vinegar"], "2 cups hard cider"));
            db.Recipes.Add(wassail);
            await db.SaveChangesAsync();
            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().NotContain(wassail.Id, "the write has not been seen yet");

            cache.Invalidate();
            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().Contain(wassail.Id);
            var afterWrite = cache.Computations;

            now = now.AddMinutes(16);
            await resolver.ResolveAsync(HouseholdId);
            cache.Computations.Should().Be(afterWrite + 2, "the household's two values older than their age are computed again");
        }

        [Fact]
        public async Task A_stale_value_is_served_while_one_refresh_runs_on_its_own_context()
        {
            var name = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;
            await using var db = new ApplicationDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var foods = await SeedAsync(db);
            db.Recipes.Add(Recipe("Trifle", (foods.Ids["Red Wine Vinegar"], "1 glass of sherry")));
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var cache = new DietGateCache();
            var resolver = new HouseholdRestrictionResolver(db, cache);
            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().HaveCount(1);

            var wassail = Recipe("Wassail", (foods.Ids["Apple Cider Vinegar"], "2 cups hard cider"));
            db.Recipes.Add(wassail);
            await db.SaveChangesAsync();
            cache.Invalidate();

            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().HaveCount(1, "the kept value is served while the refresh runs");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (cache.Computations < 4 && DateTime.UtcNow < deadline) await Task.Delay(20);
            (await resolver.ResolveAsync(HouseholdId)).RecipeIds.Should().Contain(wassail.Id);
        }

        [Fact]
        public async Task A_failed_refresh_is_logged_and_no_value_is_served_after_it_or_past_twice_its_age()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var db = new ApplicationDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var now = new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc);
            var logger = new ListLogger();
            var cache = new DietGateCache(TimeSpan.FromMinutes(15), () => now) { Logger = logger };
            var fail = false;
            var calls = 0;
            Task<string> Compute(ApplicationDbContext _)
            {
                var call = Interlocked.Increment(ref calls);
                return Volatile.Read(ref fail) ? throw new TimeoutException("probe timed out") : Task.FromResult("v" + call);
            }
            async Task UntilAsync(Func<bool> done)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!done() && DateTime.UtcNow < deadline) await Task.Delay(20);
            }

            (await cache.GetAsync(db, "drink-lines", Compute)).Should().Be("v1");
            Volatile.Write(ref fail, true);
            cache.Invalidate();
            (await cache.GetAsync(db, "drink-lines", Compute)).Should().Be("v1", "the kept value is served while its refresh runs");
            await UntilAsync(() => logger.Warnings.Count > 0);

            logger.Warnings.Should().ContainSingle().Which.Should().BeOfType<TimeoutException>();
            await FluentActions.Awaiting(() => cache.GetAsync(db, "drink-lines", Compute)).Should().ThrowAsync<TimeoutException>("a value whose refresh failed is not served again");
            Volatile.Write(ref fail, false);
            (await cache.GetAsync(db, "drink-lines", Compute)).Should().Be("v4");

            now = now.AddMinutes(20);
            (await cache.GetAsync(db, "drink-lines", Compute)).Should().Be("v4", "a value past its age but within twice it is served while its refresh runs");
            await UntilAsync(() => Volatile.Read(ref calls) >= 5);
            await UntilAsync(() => cache.Computations >= 3);
            (await cache.GetAsync(db, "drink-lines", Compute)).Should().Be("v5");

            now = now.AddMinutes(31);
            Volatile.Write(ref fail, true);
            await FluentActions.Awaiting(() => cache.GetAsync(db, "drink-lines", Compute)).Should().ThrowAsync<TimeoutException>("a value older than twice its age is computed in line, never served");
        }

        private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
        {
            public List<Exception?> Warnings { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
                    lock (Warnings) Warnings.Add(exception);
            }
        }

        [Theory]
        [InlineData("INSERT INTO recipe.\"RecipeIngredient\" (\"RecipeId\", \"IngredientId\") VALUES (@p0, @p1)", true)]
        [InlineData("UPDATE recipe.\"RecipeIngredient\" AS r SET \"IngredientId\" = @p0 WHERE r.\"RecipeId\" = @p1", true)]
        [InlineData("UPDATE recipe.\"Ingredient\" SET \"Name\" = @p0 WHERE \"Id\" = @p1", true)]
        [InlineData("INSERT INTO recipe.\"IngredientAlias\" (\"AliasName\") VALUES (@p0) RETURNING \"Id\"", true)]
        [InlineData("DELETE FROM nutrient.\"IngredientNutrient\" WHERE \"Id\" = @p0", true)]
        [InlineData("SELECT r.\"RecipeId\" FROM recipe.\"RecipeIngredient\" AS r WHERE r.\"RawLine\" ~* @p0", false)]
        [InlineData("UPDATE recipe.\"IngredientWording\" SET \"IngredientId\" = @p0", false)]
        [InlineData("UPDATE recipe.\"Recipe\" SET \"Name\" = @p0 WHERE \"Id\" = @p1", false)]
        [InlineData("INSERT INTO curation.\"FoodCatalogProposal\" (\"IngredientId\") VALUES (@p0)", false)]
        [InlineData("UPDATE recipe.\"Recipe\" SET \"Servings\" = @p0 WHERE \"Id\" = @p1", true)]
        [InlineData("UPDATE recipe.\"Recipe\" SET \"Name\" = @p0, \"RecipeServings\" = @p1 WHERE \"Id\" = @p2", true)]
        [InlineData("INSERT INTO recipe.\"Recipe\" (\"Name\", \"Servings\") VALUES (@p0, @p1) RETURNING \"Id\"", true)]
        [InlineData("UPDATE recipe.\"RecipeIngredient\" SET \"AlcoholRetention\" = @p0, \"AlcoholRetentionBasis\" = @p1 WHERE \"RecipeId\" = @p2", true)]
        [InlineData("UPDATE recipe.\"RecipeStep\" SET \"Description\" = @p0", false)]
        [InlineData("UPDATE measurement.\"Measurement\" SET \"BaseUnitConversionFactor\" = @p0, \"IsContainer\" = @p1 WHERE \"Id\" = @p2", true)]
        [InlineData("INSERT INTO measurement.\"Measurement\" (\"Name\", \"MeasurementCategoryId\") VALUES (@p0, @p1) RETURNING \"Id\"", true)]
        [InlineData("UPDATE measurement.\"MeasurementConversion\" SET \"Factor\" = @p0", false)]
        [InlineData("SELECT m.\"Id\" FROM measurement.\"Measurement\" AS m", false)]
        public void The_invalidator_knows_a_write_to_a_table_the_probes_read(string sql, bool touches)
        {
            DietGateCacheInvalidator.Touches(sql).Should().Be(touches);
        }

        [Fact]
        public void A_write_outside_a_transaction_invalidates_at_once_and_one_inside_at_its_commit()
        {
            var cache = new DietGateCache();
            var write = "UPDATE recipe.\"RecipeIngredient\" SET \"IngredientId\" = 1";
            var start = cache.Generation;

            cache.Invalidator.Executed(new StubCommand("SELECT 1 FROM recipe.\"RecipeIngredient\"", null));
            cache.Generation.Should().Be(start);
            cache.Invalidator.Executed(new StubCommand(write, null));
            cache.Generation.Should().Be(start + 1);

            var transaction = new StubTransaction();
            cache.Invalidator.Executed(new StubCommand(write, transaction));
            cache.Generation.Should().Be(start + 1, "an uncommitted write is not yet visible to a refresh");
            cache.Invalidator.Committed(new StubTransaction());
            cache.Generation.Should().Be(start + 1, "another transaction wrote nothing");
            cache.Invalidator.Committed(transaction);
            cache.Generation.Should().Be(start + 2);
            cache.Invalidator.Committed(transaction);
            cache.Generation.Should().Be(start + 2);
        }

        private sealed class StubTransaction : System.Data.Common.DbTransaction
        {
            public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
            protected override System.Data.Common.DbConnection? DbConnection => null;
            public override void Commit() { }
            public override void Rollback() { }
        }

        private sealed class StubCommand : System.Data.Common.DbCommand
        {
            public StubCommand(string text, System.Data.Common.DbTransaction? transaction)
            {
                CommandText = text;
                DbTransaction = transaction;
            }

            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string CommandText { get; set; }
            public override int CommandTimeout { get; set; }
            public override System.Data.CommandType CommandType { get; set; }
            public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
            protected override System.Data.Common.DbConnection? DbConnection { get; set; }
            protected override System.Data.Common.DbParameterCollection DbParameterCollection => throw new NotSupportedException();
            protected override System.Data.Common.DbTransaction? DbTransaction { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override void Cancel() { }
            public override int ExecuteNonQuery() => 0;
            public override object? ExecuteScalar() => null;
            public override void Prepare() { }
            protected override System.Data.Common.DbParameter CreateDbParameter() => throw new NotSupportedException();
            protected override System.Data.Common.DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => throw new NotSupportedException();
        }

        private static RecipeEntity Recipe(string name, params long[] foods)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var food in foods)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, SortOrder = order++ });
            return recipe;
        }

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }
    }
}
