using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The diet gate's stored results (staging checkpoint 5, Q2, Q3, Q6, P23): the build job decides every food and
    /// recipe per restriction set into the database, and every request reads only those rows. The store answers exactly
    /// as the matcher does (<see cref="AssertStoreAgreesAsync"/>, run by the golden diet tests); before the first build
    /// every recipe is held; a recipe not decided yet (new, or its lines written since) is held by every set; a recipe with
    /// no ingredient line and a meal not decided yet are unknown and held for a severe type only; a changed food or
    /// criterion is decided again alone, and a type keeps its previous set until the new one is built; a rule-version
    /// change builds beside the active build; and a failed cycle loses nothing stored, nor the work it left undone.
    /// </summary>
    public class DietGateStoreTests
    {
        private const long HouseholdId = 7, PersonId = 70, CeliacRef = 60800, VegetarianRef = 60850, NoAlcoholRef = 60913, AlcoholNutrient = 5038, Gram = 1;

        private readonly ITestOutputHelper _output;

        public DietGateStoreTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// Asserts that, after a build, the stored sets let through exactly the recipes and foods the in-line matcher does,
        /// for the household and for the person's severe set, apart from the recipes the store holds as unknown for a severe
        /// set (no ingredient line), which the matcher lets through; leaves the store built, so later readers of the context
        /// read it.
        /// </summary>
        internal static async Task AssertStoreAgreesAsync(ApplicationDbContext db, long householdId, long personId)
        {
            var inline = new HouseholdRestrictionResolver(db, store: false);
            var expected = new[] { await inline.ResolveAsync(householdId), await inline.ResolveSevereForPersonAsync(personId) };
            await new DietGateStoreBuilder().RunAsync(db);
            var stored = new HouseholdRestrictionResolver(db, store: true);
            var actual = new[] { await stored.ResolveAsync(householdId), await stored.ResolveSevereForPersonAsync(personId) };
            foreach (var (e, a) in expected.Zip(actual))
            {
                a.Pending.Should().BeFalse("the build is complete");
                a.HasSevere.Should().Be(e.HasSevere);
                var unknownSets = a.Stored?.UnknownSetIds.ToList() ?? new List<long>();
                var unknown = (await db.DietRecipeFlags.Where(f => unknownSets.Contains(f.SetId) && f.Verdict == DietVerdict.Unknown).Select(f => f.RecipeId).ToListAsync()).ToHashSet();
                var matcher = (await db.Recipes.WithoutHeld(db, e).Select(r => r.Id).ToListAsync()).Where(id => !unknown.Contains(id)).ToList();
                (await db.Recipes.WithoutHeld(db, a).Select(r => r.Id).ToListAsync())
                    .Should().BeEquivalentTo(matcher, "the store lets through the recipes the matcher does");
                (await db.Ingredients.WithoutHeld(db, a).Select(i => i.Id).ToListAsync())
                    .Should().BeEquivalentTo(await db.Ingredients.WithoutHeld(db, e).Select(i => i.Id).ToListAsync(), "the store counts the foods the matcher does");
            }
        }

        private static ApplicationDbContext NewContext(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptors).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private sealed record Kitchen(long Flour, long Rice, long Water, long Bread, long Pilaf, long Toast, long Buns);

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
            var buns = Recipe("Maple Pecan Sticky Buns");
            db.Recipes.AddRange(bread, pilaf, toast, buns);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return new Kitchen(flour.Id, rice.Id, water.Id, bread.Id, pilaf.Id, toast.Id, buns.Id);
        }

        private static async Task<HashSet<long>> OfferedAsync(ApplicationDbContext db, RestrictedIngredientSet set) =>
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).ToHashSet();

        [Fact]
        public async Task Before_the_first_build_every_recipe_is_held_and_after_it_the_stored_rows_decide()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var resolver = new HouseholdRestrictionResolver(db, store: true);

            var cold = await resolver.ResolveAsync(HouseholdId);
            cold.Pending.Should().BeTrue("no build is active yet");
            (await OfferedAsync(db, cold)).Should().BeEmpty("a recipe the store has not decided is never shown as fine");

            var report = await new DietGateStoreBuilder().RunAsync(db);
            report.Switched.Should().BeTrue();
            report.State.Should().Be(DietGateStore.Active);
            report.Built.Should().NotBeEmpty();

            var warm = await new HouseholdRestrictionResolver(db, store: true).ResolveAsync(HouseholdId);
            warm.Pending.Should().BeFalse();
            warm.Stored.Should().NotBeNull();
            warm.IngredientIds.Should().BeEmpty("the foods stay in the database; a request loads none of them");
            (await OfferedAsync(db, warm)).Should().BeEquivalentTo(new[] { k.Pilaf },
                "bread by its food, toast by its line's own text, and the buns, which have no ingredient line, as unknown for a severe type");
            var flags = await db.DietRecipeFlags.Where(f => warm.Stored!.SetIds.Contains(f.SetId)).ToListAsync();
            flags.Should().Contain(f => f.RecipeId == k.Bread && f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.Food && f.IngredientId == k.Flour && f.CriterionId != null);
            flags.Should().Contain(f => f.RecipeId == k.Toast && f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.Line && f.CriterionId != null);
            flags.Should().Contain(f => f.RecipeId == k.Buns && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.NoLine);
            var fact = await db.DietFoodFacts.FirstAsync(f => f.IngredientId == k.Flour && warm.Stored!.SetIds.Contains(f.SetId));
            fact.Verdict.Should().Be(DietVerdict.Contains);
            fact.Source.Should().Be("name");
            fact.CriterionIds.Should().Contain(fact.CriterionId!.Value.ToString());
        }

        [Fact]
        public async Task A_recipe_with_no_ingredient_line_is_unknown_held_for_a_severe_type_and_counted_but_shown_for_a_diet()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 8, PersonId = 80, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = 80, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            (await OfferedAsync(db, severe)).Should().NotContain(k.Buns, "Celiac Disease is severe, and a recipe with no line cannot be shown to be free of gluten (Q6)");
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId))).Should().NotContain(k.Buns);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().Contain(k.Buns, "a diet of severity 3 holds what contains meat, not what it cannot read");

            var counts = await DietGateStoreReports.CountsAsync(db);
            var celiac = counts.Types.Single(t => t.TypeId == CeliacRef && t.Reading == "all");
            celiac.Severe.Should().BeTrue();
            celiac.RecipesUnknown.Should().Be(1);
            celiac.RecipesHeldOnlyForUnknown.Should().Be(1, "the price of the safe side is counted per severe type");
            celiac.RecipesByFood.Should().Be(1);
            celiac.RecipesByText.Should().Be(1);
            celiac.RecipesHeld.Should().Be(3);
            counts.Types.Single(t => t.TypeId == VegetarianRef && t.Reading == "all").RecipesHeldOnlyForUnknown.Should().Be(0);
            counts.Recipes.Should().Be(4);

            var page = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);
            (await page.GetDietMatchesAsync(k.Buns, PersonId)).Should().ContainSingle(m => m.IngredientName == RecipeOrchestrationService.DietUnknownName && m.Severity == 4);
            (await page.GetDietMatchesAsync(k.Pilaf, PersonId)).Should().BeEmpty();
            (await page.GetDietMatchesAsync(k.Bread, PersonId)).Should().Contain(m => m.IngredientId == k.Flour);
        }

        [Fact]
        public async Task A_book_recipe_whose_title_or_method_names_what_its_lines_omit_is_held_with_its_own_reason_and_a_recipe_written_in_the_app_is_read_by_its_lines()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var egg = new IngredientEntity { Name = "Eggs", CurationStatusId = (long)CurationStatusEnum.Curated };
            var cheese = new IngredientEntity { Name = "Cheddar Cheese", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(egg, cheese);
            await db.SaveChangesAsync();
            var imported = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var sandwich = Recipe("EGG AND BROWN BREAD SANDWICH", (egg.Id, "2 hard-boiled eggs"));
            sandwich.ScrapedAtUtc = imported;
            sandwich.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Chop the eggs fine and season." } };
            var scones = Recipe("Scones", (cheese.Id, "1 cup milk"), (egg.Id, "1 egg"));
            scones.ScrapedAtUtc = imported;
            scones.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Sift the flour with the baking powder. Stir in the milk and the egg." } };
            var chickenRice = Recipe("Chicken and Rice", (k.Rice, string.Empty), (k.Water, string.Empty));
            var lasagna = Recipe("Zucchini Lasagna", (cheese.Id, "1 cup ricotta"));
            lasagna.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Layer the zucchini where the lasagna noodles would go." } };
            var pizza = Recipe("Cauliflower Crust Pizza", (k.Rice, "1 head cauliflower, riced"));
            db.Recipes.AddRange(sandwich, scones, chickenRice, lasagna, pizza);
            await db.SaveChangesAsync();
            await new DietGateStoreBuilder().RunAsync(db);

            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            var offered = await OfferedAsync(db, severe);
            offered.Should().NotContain(new[] { sandwich.Id, scones.Id }, "a book's sandwich has bread though the lines leave it out (review ck11-dc-3), and its scones the flour the method names (R3)");
            offered.Should().Contain(new[] { chickenRice.Id, lasagna.Id, pizza.Id },
                "a recipe written in the app stores its lines as foods with no text and is read by them (ck11-dc-1), and its title and method are not read (ck11-dc-2)");
            var flags = await db.DietRecipeFlags.Where(f => severe.Stored!.SetIds.Contains(f.SetId)).ToListAsync();
            flags.Should().Contain(f => f.RecipeId == sandwich.Id && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.Title && f.CriterionId != null);
            flags.Should().Contain(f => f.RecipeId == scones.Id && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.Method && f.CriterionId != null);
            flags.Should().NotContain(f => f.RecipeId == chickenRice.Id || f.RecipeId == lasagna.Id || f.RecipeId == pizza.Id);

            var counts = await DietGateStoreReports.CountsAsync(db);
            var celiac = counts.Types.Single(t => t.TypeId == CeliacRef && t.Reading == "all");
            celiac.RecipesByTitle.Should().Be(1);
            celiac.RecipesByMethod.Should().Be(1);
            celiac.RecipesNoLine.Should().Be(1, "only the buns, which have no line at all");
            celiac.RecipesUnknown.Should().Be(celiac.RecipesByTitle + celiac.RecipesByMethod + celiac.RecipesNoLine + celiac.RecipesUnknownFood);

            var page = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);
            (await page.GetDietMatchesAsync(sandwich.Id, PersonId)).Should().ContainSingle(m => m.IngredientName == "Named in the title" && m.Notes!.Contains("\"sandwich\""));
            (await page.GetDietMatchesAsync(scones.Id, PersonId)).Should().ContainSingle(m => m.IngredientName == "Named in the method" && m.Notes!.Contains("\"flour\""));
            (await page.GetDietMatchesAsync(chickenRice.Id, PersonId)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_recipe_not_yet_decided_is_held_for_every_restriction_and_the_next_cycle_decides_it()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 8, PersonId = 80, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = 80, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);

            var risotto = Recipe("Risotto", (k.Rice, "1 cup arborio rice"));
            db.Recipes.Add(risotto);
            await db.SaveChangesAsync();

            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId))).Should().NotContain(risotto.Id, "not decided yet: unknown, held for a severe type");
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().NotContain(risotto.Id, "a recipe not decided yet could hold meat; no set lets it through until it is decided (review CK10S-3)");

            var report = await builder.RunAsync(db);
            report.RecipesDecided.Should().Be(1, "only the new recipe is read again");
            report.FoodsDecided.Should().Be(0);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId))).Should().Contain(risotto.Id);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().Contain(risotto.Id);
        }

        [Fact]
        public async Task A_recipe_whose_lines_are_written_is_held_at_once_by_every_set_until_the_build_decides_it_again()
        {
            using var db = NewContext(DietRecipeStateClearer.Shared);
            var k = await SeedAsync(db);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 8, PersonId = 80, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = 80, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            var beef = new IngredientEntity { Name = "Beef, ground", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(beef);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId))).Should().Contain(k.Pilaf);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().Contain(k.Pilaf);

            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = k.Pilaf, IngredientId = k.Flour, MeasurementId = Gram, Quantity = 1m, RawLine = "1 tbsp flour", SortOrder = 5 });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = k.Pilaf, IngredientId = beef.Id, MeasurementId = Gram, Quantity = 1m, RawLine = "1 lb ground beef", SortOrder = 6 });
            await db.SaveChangesAsync();

            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId))).Should().NotContain(k.Pilaf,
                "a line on a food whose fact is contains was added, and the decision on the old lines is no longer read");
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().NotContain(k.Pilaf, "a severity-3 diet holds an edited recipe too");

            var report = await builder.RunAsync(db);
            report.RecipesDecided.Should().Be(1);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId))).Should().NotContain(k.Pilaf);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().NotContain(k.Pilaf);

            var rice = await db.RecipeIngredients.SingleAsync(l => l.RecipeId == k.Toast);
            rice.LastModifiedDate = DateTime.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().NotContain(k.Toast);
            report = await builder.RunAsync(db);
            report.RecipesDecided.Should().Be(1, "a recipe with no stored state is decided again even when what its fingerprint reads did not change");
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().Contain(k.Toast);
            await AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_changed_food_is_decided_again_alone_and_the_recipes_on_it_follow_in_the_same_cycle()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId))).Should().Contain(k.Pilaf);

            var rice = await db.Ingredients.SingleAsync(i => i.Id == k.Rice);
            rice.Name = "Wheat Berries";
            await db.SaveChangesAsync();
            var report = await builder.RunAsync(db);

            report.FoodsDecided.Should().Be(1, "only the renamed food is decided again");
            report.RecipesDecided.Should().Be(2, "the pilaf and the toast are on it");
            report.Switched.Should().BeFalse();
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId))).Should().NotContain(k.Pilaf);
            var again = await builder.RunAsync(db);
            again.FoodsDecided.Should().Be(0);
            again.RecipesDecided.Should().Be(0, "nothing changed");
            await AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task An_alias_or_a_statement_of_a_food_is_part_of_its_fingerprint()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);

            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = k.Water, AliasName = "barley water", SourceContext = "admin:test" });
            await db.SaveChangesAsync();
            var report = await builder.RunAsync(db);

            report.FoodsDecided.Should().Be(1);
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId))).Should().NotContain(k.Pilaf, "the water it uses now counts as barley by an admin's alias");
            await AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_changed_criterion_keeps_its_types_previous_set_until_the_new_one_is_built_and_the_old_set_is_then_removed()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            var before = await db.DietGateSets.Where(s => s.RestrictionTypeId == CeliacRef).Select(s => s.Id).ToListAsync();

            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = CeliacRef, IngredientPattern = "%rice%", Severity = 4, Notes = "admin", CreatedDate = DateTime.UtcNow });
            await db.SaveChangesAsync();
            foreach (var kept in new[] { await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId), await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId) })
            {
                kept.Pending.Should().BeFalse("a criterion changed by a deploy or an admin never empties the gate (review CK10S-2)");
                kept.Stored!.SetIds.Should().BeSubsetOf(before, "the type's previous set is read until the new one is built");
                (await OfferedAsync(db, kept)).Should().BeEquivalentTo(new[] { k.Pilaf });
            }

            builder.BeforeRecipes = _ => throw new InvalidOperationException("the cycle fails while the new set is building");
            await builder.Invoking(b => b.RunAsync(db)).Should().ThrowAsync<InvalidOperationException>();
            builder.BeforeRecipes = null;
            (await db.DietGateSets.Where(s => before.Contains(s.Id)).Select(s => s.State).ToListAsync()).Should().AllBe(DietGateStore.Superseded);
            var building = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            building.Pending.Should().BeFalse("the superseded set is still read while its successor is not complete");
            (await OfferedAsync(db, building)).Should().BeEquivalentTo(new[] { k.Pilaf });
            (await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId)).Pending.Should().BeFalse();

            await builder.RunAsync(db);
            var after = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            after.Stored!.SetIds.Should().NotIntersectWith(before);
            after.Pending.Should().BeFalse();
            (await OfferedAsync(db, after)).Should().NotContain(k.Pilaf);
            (await db.DietGateSets.AnyAsync(s => before.Contains(s.Id))).Should().BeFalse("a set no type reads any more is removed with its rows");
            (await db.DietRecipeFlags.AnyAsync(f => before.Contains(f.SetId))).Should().BeFalse();
            await AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_new_rule_version_is_built_beside_the_active_build_which_requests_read_until_the_switch()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);
            var first = await DietGateStore.ActiveBuildAsync(db);

            db.DietGateBuilds.Add(new DietGateBuildEntity { Version = "next", State = DietGateStore.Building, StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var during = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            during.Pending.Should().BeFalse("a build in progress never empties the gate");
            during.Stored!.BuildId.Should().Be(first!.Value);
            (await OfferedAsync(db, during)).Should().BeEquivalentTo(new[] { k.Pilaf });

            var report = await new DietGateStoreBuilder(() => "next").RunAsync(db);
            report.Switched.Should().BeTrue();
            var second = await DietGateStore.ActiveBuildAsync(db);
            second.Should().NotBe(first.Value);
            (await db.DietGateBuilds.SingleAsync(b => b.Id == first.Value)).State.Should().Be(DietGateStore.Retired);
            (await db.DietGateSets.AnyAsync(s => s.BuildId == first.Value)).Should().BeFalse("the retired build's rows are removed");
            (await db.DietRecipeStates.AnyAsync(s => s.BuildId == first.Value)).Should().BeFalse();
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId))).Should().BeEquivalentTo(new[] { k.Pilaf });
            (await DietGateStoreReports.BuildsAsync(db)).Should().ContainSingle(b => b.Id == second && b.State == DietGateStore.Active && b.TotalSeconds != null && b.SetReports.All(s => s.RecipeSeconds != null));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_cycle_that_fails_after_its_foods_are_stored_leaves_their_recipes_to_the_next_cycle(bool restart)
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            var rice = await db.Ingredients.SingleAsync(i => i.Id == k.Rice);
            rice.Name = "Wheat Berries";
            await db.SaveChangesAsync();

            using var stopping = new CancellationTokenSource();
            builder.BeforeRecipes = _ =>
            {
                stopping.Cancel();
                stopping.Token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            };
            await builder.Invoking(b => b.RunAsync(db, stopping.Token)).Should().ThrowAsync<OperationCanceledException>();
            builder.BeforeRecipes = null;

            var kept = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            kept.Pending.Should().BeFalse("what was stored is still served after a failed cycle");
            (await db.DietFoodFacts.AnyAsync(f => f.IngredientId == k.Rice && kept.Stored!.SetIds.Contains(f.SetId) && f.Verdict == DietVerdict.Contains))
                .Should().BeTrue("the food phase stored the renamed food's new fact before the cycle stopped");

            var next = restart ? new DietGateStoreBuilder() : builder;
            var report = await next.RunAsync(db);
            report.FoodsDecided.Should().Be(1, "the renamed food's fingerprint was not stored, so the next cycle still sees it as changed (review CK10S-1)");
            report.RecipesDecided.Should().Be(2, "the pilaf and the toast are on it");
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId))).Should().NotContain(k.Pilaf, "wheat berries hold gluten");
            (await next.RunAsync(db)).FoodsDecided.Should().Be(0);
            await AssertStoreAgreesAsync(db, HouseholdId, PersonId);
        }

        [Fact]
        public async Task A_prepared_meal_the_build_has_not_decided_is_pending_for_a_severe_household()
        {
            using var db = NewContext();
            await SeedAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);
            var meal = new IngredientEntity { Name = "Frozen Lasagna", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 22 };
            db.Ingredients.Add(meal);
            await db.SaveChangesAsync();
            db.IngredientContents.Add(new IngredientContentsEntity { IngredientId = meal.Id, Source = "fdc:1", Statement = "Ingredients: rice.", Outcome = IngredientContentsOutcome.Read, IngredientsJson = "[]", CreatedDate = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            var verdicts = await PreparedContentsGate.JudgeAsync(db, HouseholdId, set, new[] { meal.Id });

            verdicts[meal.Id].Held.Should().Be(PreparedContentsHeld.Pending, "a meal the build has not decided is unknown, held for a severe type");
        }

        [Fact]
        public async Task Retained_alcohol_and_drink_lines_are_stored_per_recipe_as_the_matcher_reads_them()
        {
            using var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" });
            db.Nutrients.Add(new NutrientEntity { Id = AlcoholNutrient, Name = "Alcohol", DefaultMeasurementId = Gram });
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(rice);
            await db.SaveChangesAsync();
            var custard = Recipe("Custard", (rice.Id, "1 cup rice"));
            var trifle = Recipe("Trifle", (rice.Id, "1 glass of sherry"));
            db.Recipes.AddRange(custard, trifle);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            await AssertStoreAgreesAsync(db, HouseholdId, PersonId);
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            (await OfferedAsync(db, set)).Should().BeEquivalentTo(new[] { custard.Id });
            (await db.DietRecipeFlags.SingleAsync(f => f.RecipeId == trifle.Id && set.Stored!.SetIds.Contains(f.SetId))).Reason.Should().Be(DietFlagReason.Retained);
        }

        private static readonly string[] Names =
        {
            "Wheat Flour", "Flour, almond", "Flour, wheat, all-purpose", "Buckwheat flour", "Rice", "Bread, white", "Sweetbreads, veal", "Graham crackers", "Eggplant",
            "Eggs, whole", "Egg noodles", "Goat cheese", "Cheese, goat", "Oats, rolled", "Milk, whole", "Coconut milk", "Butter, salted", "Peanut butter", "Butter beans",
            "Pork chops", "Bacon", "Turkey bacon", "Beef, ground", "Chicken broth", "Shrimp", "Anchovies", "Tuna, canned", "Soy sauce", "Tamari", "Tofu",
            "Red wine vinegar", "Dry white wine", "Sherry", "Rum, dark", "Rum extract", "Ginger", "Gin", "Bread crumbs", "Beer", "Root beer", "Vanilla extract",
            "Sauerkraut", "Raisins", "Dried apricots", "Pickles, dill", "Pineapple", "Apples", "Walnuts", "Pecans", "Almonds", "Cashews", "Pine nuts", "Pesto",
            "Sesame seeds", "Tahini", "Corn", "Cornstarch", "Potatoes", "Tomatoes", "Bell pepper", "Paprika", "Gelatin", "Marshmallows", "Honey", "Water",
            "Salt", "Sugar", "Chocolate", "Coffee", "Green tea", "Lard", "Suet", "Spaghetti", "Tortellini", "Wonton wrappers", "Spanish rice", "Pizza dough",
        };

        private static readonly (string Text, string? Context)[] AliasPool =
        {
            ("wheat flour", "admin:x"), ("bacon or butter", null), ("almond flour", "fdc:description"), ("Flour, almond", "fdc:description"), ("a wine glassful of salt", "deterministic:recheck:x"),
            ("dark rum", null), ("sherry wine", "admin:x"), ("barley water", "deterministic:x"), ("pork fat", "retired:x"), ("little sugar and brandy", null), ("egg", "link:deterministic:x"),
            ("Wineglass Water", "admin:x"), ("gin and tonic", "admin:x"), ("ginger root", "admin:x"), ("milk powder", "fdc:description"), ("Cheese, goat", "fdc:description"),
        };

        [Fact]
        public async Task The_food_reader_decides_every_default_type_as_the_matchers_probe_does()
        {
            using var db = NewContext();
            db.Nutrients.Add(new NutrientEntity { Id = AlcoholNutrient, Name = "Alcohol", DefaultMeasurementId = Gram });
            long typeId = 61000;
            foreach (var name in DefaultRestrictionCriteria.ByName.Keys) db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId++, Name = name });
            var foods = new List<IngredientEntity>();
            for (var i = 0; i < Names.Length * 3; i++)
            {
                var food = new IngredientEntity { Name = Names[i % Names.Length] + (i >= Names.Length ? " " + (i / Names.Length) : string.Empty), CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = i % 11 == 0 ? 10 : i % 13 == 0 ? 2 : null };
                if (i % 4 == 1) food.Aliases.Add(new IngredientAliasEntity { AliasName = AliasPool[i % AliasPool.Length].Text, SourceContext = AliasPool[i % AliasPool.Length].Context });
                if (i % 9 == 2) food.Aliases.Add(new IngredientAliasEntity { AliasName = AliasPool[(i + 5) % AliasPool.Length].Text, SourceContext = AliasPool[(i + 5) % AliasPool.Length].Context });
                if (Names[i % Names.Length] is "Dry white wine" or "Sherry" or "Beer" or "Rum extract" || i % 17 == 3)
                    food.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = AlcoholNutrient, Amount = i % 2 == 0 ? 10m : 0.2m, MeasurementId = Gram });
                foods.Add(food);
            }
            db.Ingredients.AddRange(foods);
            await db.SaveChangesAsync();
            await new PreparedContentsService(db).RecordAsync(new[]
            {
                new ContentsStatement(foods[0].Id, "fdc:1", "Ingredients: enriched flour, water, salt. Contains: wheat."),
                new ContentsStatement(foods[4].Id, "fdc:2", "Ingredients: rice, cheddar cheese (milk), vegetable oil."),
                new ContentsStatement(foods[6].Id, "fdc:3", "Ingredients: sugar, gelatin. May contain peanuts."),
                new ContentsStatement(foods[10].Id, "fdc:4", "Ingredients: corn, salt."),
            }, "store-test", apply: true);
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var typeIds = await db.Set<RestrictionCriterionEntity>().Select(c => c.RestrictionTypeId).Distinct().ToListAsync();
            var criteria = await DietCriteriaMatcher.CriteriaAsync(db, typeIds);
            var catalog = await DietFoodCatalog.LoadAsync(db, criteria.Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).ToList());
            await catalog.LoadStatementsAsync(db, catalog.Foods.Keys);
            var reading = DietFoodReader.Read(catalog, catalog.Foods.Keys.ToHashSet());
            var compared = 0;
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId))
            {
                foreach (var list in new[] { type.ToList(), type.Where(c => c.Severity >= 4).ToList() }.Where(l => l.Count > 0))
                {
                    var probe = (await DietCriteriaMatcher.ProbeAsync(db, list))[type.Key];
                    var decision = DietFoodReader.Decide(reading, list);
                    foreach (var c in list)
                        (decision.Foods.GetValueOrDefault(c.Id) ?? new HashSet<long>()).Should().BeEquivalentTo(probe.Foods.GetValueOrDefault(c.Id) ?? new HashSet<long>(),
                            $"{c.TypeName} {c.LikePattern ?? c.WordPattern ?? c.NutrientName} counts the foods the probe counts");
                    decision.AliasOnly.Should().BeEquivalentTo(probe.AliasOnly, $"{type.First().TypeName} reads the same aliases as not counting");
                    compared++;
                }
            }
            _output.WriteLine($"{compared} sets over {foods.Count} foods compared");
            compared.Should().BeGreaterThan(50);
        }

        [Fact]
        public void The_ops_count_reads_the_stored_rows_by_default_and_the_rule_audit_only_when_asked()
        {
            var script = System.IO.File.ReadAllText(ScriptPath());
            var audit = script.IndexOf("\\if :{?audit}", StringComparison.Ordinal);
            var end = script.IndexOf("\\q\n\\endif", StringComparison.Ordinal);
            audit.Should().BeGreaterThan(0, "ops/diet-gate-count.sql reads the stored rows unless -v audit=1 is given (P23)");
            end.Should().BeGreaterThan(audit);
            var stored = script[audit..end];
            stored.Should().Contain("plan.\"DietGateBuild\"").And.Contain("plan.\"DietRecipeFlag\"").And.Contain("plan.\"DietFoodFact\"").And.Contain("plan.\"DietRecipeState\"");
            stored.Should().Contain("\"State\" = 'active'").And.Contain("BEGIN READ ONLY").And.Contain("ROLLBACK");
            stored.Should().NotContain("~*", "the stored count runs no pattern");
            stored.Should().Contain("\"Verdict\" = 1 AND f.\"Reason\" = 1").And.Contain("\"Verdict\" = 2");
            ((int)DietVerdict.Contains).Should().Be(1);
            ((int)DietVerdict.Unknown).Should().Be(2);
            ((int)DietFlagReason.Food).Should().Be(1);
            script[end..].Should().Contain("rule_pats", "the crude-versus-rule audit follows, unchanged, for -v audit=1");
        }

        private static string ScriptPath([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "..", "..", "..", "ops", "diet-gate-count.sql"));

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public List<string> Sql { get; } = new();

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Sql.Add(command.CommandText);
                throw new OperationCanceledException("captured");
            }
        }

        private sealed class NoConnection : DbConnectionInterceptor
        {
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private async Task<string> CaptureAsync(string name, Func<ApplicationDbContext, Task> run)
        {
            var capture = new CaptureAndStop();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var act = () => run(db);
            await act.Should().ThrowAsync<OperationCanceledException>();
            capture.Sql.Should().NotBeEmpty();
            _output.WriteLine($"-- {name}\n{capture.Sql[0]};\n");
            return capture.Sql[0];
        }

        [Fact]
        public async Task A_request_reads_only_the_indexed_rows_and_its_filters_translate_to_postgres()
        {
            var set = new RestrictedIngredientSet { Stored = new StoredDietGate(3, new long[] { 11, 12 }, new long[] { 12 }, new Dictionary<long, long>()) };
            set.IngredientIds.Add(5);
            var recipes = await CaptureAsync("recipes", db => db.Recipes.WithoutHeld(db, set).OrderBy(r => r.Id).Take(20).Select(r => r.Id).ToListAsync());
            recipes.Should().Contain("\"DietRecipeFlag\"").And.Contain("\"DietRecipeState\"").And.NotContain("~*", "no request runs a pattern over names or lines");
            var foods = await CaptureAsync("foods", db => db.Ingredients.WithoutHeld(db, set).Select(i => i.Id).ToListAsync());
            foods.Should().Contain("\"DietFoodFact\"").And.Contain("\"DietFoodState\"").And.NotContain("~*");
            await CaptureAsync("sets", db => DietGateStore.SetsAsync(db, new[] { (IReadOnlyList<DietDataCriterion>)new[] { new DietDataCriterion(1, 2, null, null, null, null, 4, null, "%wheat%", "Celiac Disease") } }, new HashSet<long>()));
            await CaptureAsync("counts", db => DietGateStoreReports.CountsAsync(db));
        }
    }
}
