using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The diet gate's name patterns (<c>%peanut%</c>, <c>%pork%</c>, an admin's word pattern) by the
    /// rule staging checkpoint 1 (K9) set for alcohol: a food counts by its own name, its USDA
    /// category or an alias a rule or a person gave it, never by a recipe line stored as an alias
    /// with no source; a line on a food matched only through such an alias decides for its own
    /// recipe, in plans, "Surprise me", the severe search filter and on the recipe page.
    /// </summary>
    public class DietGatePatternTests
    {
        private const long HouseholdId = 7, PersonId = 70, PeanutRef = 2013, HalalRef = 60914, OffalRef = 69001, Gram = 1, Approved = 990001;
        private const long Curated = (long)CurationStatusEnum.Curated;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeOrchestrationService Page(ApplicationDbContext db) =>
            new(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

        private static async Task SeedAsync(ApplicationDbContext db, long typeId, string name)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Persons.Add(new PersonEntity { Id = PersonId, Name = "Cook" });
            db.Set<ReferenceEntity>().AddRange(new ReferenceEntity { Id = typeId, Name = name }, new ReferenceEntity { Id = Approved, Name = "Approved" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = name, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
        }

        private static async Task<long> FoodAsync(ApplicationDbContext db, string name, int? category = null, params (string Alias, string? Context)[] aliases)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = Curated, FdcCategoryId = category };
            foreach (var (alias, context) in aliases) food.Aliases.Add(new IngredientAliasEntity { AliasName = alias, SourceContext = context });
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food.Id;
        }

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = name, AuthorId = PersonId, CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public,
                RecipeIngredients = new List<RecipeIngredientEntity>(),
            };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        [Fact]
        public async Task A_peanut_alias_that_is_a_recipe_line_keeps_only_its_own_recipe_out_of_plans_surprise_me_and_the_severe_search()
        {
            using var db = NewContext();
            await SeedAsync(db, PeanutRef, "Peanut Allergy");
            var oil = await FoodAsync(db, "Vegetable Oil", 4, ("vegetable or peanut oil", null), ("oil, peanut or canola", "link:ai:qwen2.5:14b-instruct"));
            var groundnut = await FoodAsync(db, "Groundnut Oil", 4, ("peanut oil", "admin:review"));
            var stirFry = Recipe("Stir fry", (oil, "2 tbsp vegetable oil"));
            var satay = Recipe("Satay", (oil, "1/4 cup vegetable or peanut oil"));
            var fried = Recipe("Fried plantain", (groundnut, "oil for frying"));
            db.Recipes.AddRange(stirFry, satay, fried);
            await db.SaveChangesAsync();

            var resolver = new HouseholdRestrictionResolver(db);
            var plans = await resolver.ResolveAsync(HouseholdId);
            var severe = await resolver.ResolveSevereForPersonAsync(PersonId);

            plans.IngredientIds.Should().Contain(groundnut, "an alias an admin gave the food counts").And.NotContain(oil, "an alias with no source, or one a model pick left, is a recipe line");
            plans.RecipeIds.Should().Contain(satay.Id, "the line offers peanut oil itself").And.NotContain(stirFry.Id);
            severe.IngredientIds.Should().Contain(groundnut).And.NotContain(oil);
            severe.RecipeIds.Should().Contain(satay.Id).And.NotContain(stirFry.Id);

            var search = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);
            (await search.GetRandomRecipesAsync(10, HouseholdId)).Results.Select(r => r.Name).Should().Equal("Stir fry");
            (await search.SearchRecipesAsync(new RecipeSearchModel { RequesterPersonId = PersonId, IncludeIngredients = false })).Results.Select(r => r.Name).Should().Equal("Stir fry");

            var page = Page(db);
            (await page.GetDietMatchesAsync(stirFry.Id, PersonId)).Should().BeEmpty();
            (await page.GetDietMatchesAsync(satay.Id, PersonId)).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { RestrictionName = "Peanut Allergy", IngredientId = oil, IngredientName = "1/4 cup vegetable or peanut oil", Severity = (int?)5 });
            (await page.GetDietMatchesAsync(fried.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("Groundnut Oil");
            (await page.GetSevereDietConflictsAsync(new[] { stirFry.Id, satay.Id, fried.Id }, PersonId)).Keys.Should().BeEquivalentTo(new[] { satay.Id, fried.Id });
        }

        [Fact]
        public async Task A_recipe_the_alias_flagged_keeps_its_flag_when_another_of_its_lines_names_the_restricted_thing_on_a_wrong_food()
        {
            using var db = NewContext();
            await SeedAsync(db, HalalRef, "Halal");
            var butter = await FoodAsync(db, "Butter", 1, ("bacon or butter", null));
            var oil = await FoodAsync(db, "Olive Oil", 4);
            var beans = Recipe("Boston beans", (butter, "2 tbsp butter"), (oil, "4 slices bacon, diced"));
            var shortbread = Recipe("Shortbread", (butter, "1 cup butter"), (oil, "1 tbsp olive oil"));
            db.Recipes.AddRange(beans, shortbread);
            await db.SaveChangesAsync();

            var plans = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            plans.IngredientIds.Should().NotContain(new[] { butter, oil });
            plans.RecipeIds.Should().Contain(beans.Id, "the bacon line still linked to the oil names pork itself").And.NotContain(shortbread.Id);
            (await Page(db).GetDietMatchesAsync(beans.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("4 slices bacon, diced");
            (await Page(db).GetDietMatchesAsync(shortbread.Id, PersonId)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_pork_food_counts_by_its_usda_category_and_its_usda_description_though_its_name_does_not_say_pork()
        {
            using var db = NewContext();
            await SeedAsync(db, HalalRef, "Halal");
            var fatback = await FoodAsync(db, "Fatback", 10);
            var butt = await FoodAsync(db, "Boston Butt", null, ("Pork, fresh, shoulder, blade, boston (roasts), separable lean and fat, raw", "fdc:description"));
            var beans = await FoodAsync(db, "Navy Beans", 16);
            var cassoulet = Recipe("Cassoulet", (fatback, "4 oz fatback"), (beans, "2 cups navy beans"));
            var pulled = Recipe("Pulled shoulder", (butt, "4 lb boston butt"));
            db.Recipes.AddRange(cassoulet, pulled);
            await db.SaveChangesAsync();

            var plans = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            plans.IngredientIds.Should().Contain(new[] { fatback, butt }).And.NotContain(beans);
            (await Page(db).GetDietMatchesAsync(cassoulet.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("Fatback");
            (await Page(db).GetDietMatchesAsync(pulled.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("Boston Butt");
        }

        [Fact]
        public async Task An_admins_word_pattern_reads_an_alias_only_when_a_rule_or_a_person_gave_it()
        {
            using var db = NewContext();
            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = OffalRef, IngredientPattern = @"\mtripe\M", Severity = 3, Notes = "admin" });
            await db.SaveChangesAsync();
            await SeedAsync(db, OffalRef, "No Offal");
            var honeycomb = await FoodAsync(db, "Honeycomb", 10, ("beef tripe", "admin:review"));
            var stock = await FoodAsync(db, "Beef Stock", 6, ("beef stock or tripe broth", null));
            var menudo = Recipe("Menudo", (stock, "2 lb tripe, cut up"));
            var soup = Recipe("Onion soup", (stock, "1 quart beef stock"));
            db.Recipes.AddRange(menudo, soup);
            await db.SaveChangesAsync();

            var plans = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            plans.IngredientIds.Should().Contain(honeycomb).And.NotContain(stock);
            plans.RecipeIds.Should().Contain(menudo.Id).And.NotContain(soup.Id);
        }

        [Fact]
        public void The_name_pattern_probes_translate_to_ilike_on_names_aliases_with_their_source_and_the_lines_of_recipes_touching_given_foods()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var foods = new List<long> { 1, 2 };

            DietCriteriaMatcher.LikeNameRows(db, "%pork%", null).ToQueryString().Should().Contain("\"Name\" ILIKE").And.NotContain("\"AliasName\"");
            DietCriteriaMatcher.LikeAliasRows(db, "%pork%", foods).ToQueryString().Should().Contain("\"AliasName\" ILIKE").And.Contain("\"SourceContext\"").And.NotContain("JOIN");
            DietCriteriaMatcher.LikeLineRows(db, "%pork%", foods).ToQueryString().Should().Contain("\"RawLine\" ILIKE").And.Contain("EXISTS").And.Contain("\"IngredientId\" = ANY").And.NotContain("JOIN");
            DietCriteriaMatcher.WordLineRows(db, @"\mtripe\M", foods).ToQueryString().Should().Contain("\"RawLine\" ~*").And.Contain("EXISTS").And.NotContain("JOIN");
        }

        [Fact]
        public void A_line_names_a_pattern_by_ilike_or_by_a_counted_word()
        {
            var pork = new DietDataCriterion(1, HalalRef, null, null, null, null, 3, null, "%pork%");
            var wine = new DietDataCriterion(2, HalalRef, @"\mwines?\M", null, null, null, 3, null);

            DietCriteriaMatcher.LineNames(pork, "1/2 lb salt pork, diced").Should().BeTrue();
            DietCriteriaMatcher.LineNames(pork, "2 cups butter").Should().BeFalse();
            DietCriteriaMatcher.LineNames(wine, "1 cup dry white wine").Should().BeTrue();
            DietCriteriaMatcher.LineNames(wine, "2 tbsp red wine vinegar").Should().BeFalse("a wine vinegar is not a drink");
            DietCriteriaMatcher.LikeRegex("oat%").IsMatch("Oatmeal").Should().BeTrue();
            DietCriteriaMatcher.LikeRegex("oat%").IsMatch("Goat cheese").Should().BeFalse();
        }
    }
}
