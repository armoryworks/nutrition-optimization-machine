using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
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
    /// The diet gate's three-valued reading of a line no curated food decides (staging checkpoint 7, S3): a severe set shows
    /// such a line only when every food word of it is known from plain curated foods it leaves clear, flags it when a word is
    /// known only on foods it flags or the line holds a processing kind of the set, and holds it as unknown, with its own
    /// reason and count, otherwise; a diet of choice keeps today's reading; and a change to the curated foods' words decides
    /// every recipe again.
    /// </summary>
    public class DietFoodWordsStoreTests
    {
        private const long HouseholdId = 7, PersonId = 70, CeliacRef = 60800, SulfiteRef = 60740, VegetarianRef = 60850, Gram = 1;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private static IngredientEntity Curated(string name, int category, string? description = null) =>
            new() { Name = name, FdcCategoryId = category, CurationStatusId = (long)CurationStatusEnum.Curated, Aliases = description == null ? new List<IngredientAliasEntity>() : new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } } };

        private static IngredientEntity Wording(string name) => new() { Name = name, CurationStatusId = (long)CurationStatusEnum.NonCurated };

        private static async Task SeedAsync(ApplicationDbContext db, string type, long typeId)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId, Name = type });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = typeId });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 8, PersonId = 80, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = 80, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            await db.SaveChangesAsync();
        }

        private static async Task<HashSet<long>> OfferedAsync(ApplicationDbContext db, RestrictedIngredientSet set) =>
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).ToHashSet();

        [Fact]
        public async Task A_line_no_curated_food_decides_is_shown_to_a_severe_set_only_when_understood_and_held_otherwise_with_its_own_reason()
        {
            using var db = NewContext();
            await SeedAsync(db, "Celiac Disease", CeliacRef);
            var rice = Curated("Rice", 20, "Rice, white, long-grain, raw");
            var water = Curated("Water", 14, "Water, tap");
            var eggs = Curated("Eggs", 1, "Egg, whole, raw, fresh");
            var cookies = Curated("Cookies, brownies, commercially prepared", 18, "Cookies, brownies, commercially prepared");
            var fingers = Wording("sponge-fingers");
            var gaufrettes = Wording("rolled gaufrettes");
            var riceWater = Wording("rice water");
            var brownie = Wording("Salted Caramel Skillet Brownie");
            db.Ingredients.AddRange(rice, water, eggs, cookies, fingers, gaufrettes, riceWater, brownie);
            await db.SaveChangesAsync();
            var pudding = Recipe("Gooseberry Pudding", (eggs.Id, "8 eggs"), (fingers.Id, "4 sponge-fingers"));
            var marie = Recipe("Marie-Rose Pudding", (gaufrettes.Id, "rolled gaufrettes"));
            var drink = Recipe("Rice Water", (riceWater.Id, "2 cups rice water, chopped"), (water.Id, "1 cup water"));
            var shake = Recipe("Salted Caramel Brownie Milkshake", (brownie.Id, "1 Salted Caramel Skillet Brownie"));
            db.Recipes.AddRange(pudding, marie, drink, shake);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            var offered = await OfferedAsync(db, severe);
            offered.Should().Contain(drink.Id, "rice and water are known from plain curated foods Celiac Disease leaves clear, and \"chopped\" is a line word");
            offered.Should().NotContain(new[] { pudding.Id, marie.Id, shake.Id }, "no listed term names sponge-fingers, gaufrettes or a skillet brownie, and an unlisted word is no longer safe (S3)");
            var flags = await db.DietRecipeFlags.Where(f => severe.Stored!.SetIds.Contains(f.SetId)).ToListAsync();
            flags.Should().Contain(f => f.RecipeId == marie.Id && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.UnknownWord && f.IngredientId == gaufrettes.Id);
            flags.Should().Contain(f => f.RecipeId == shake.Id && f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.FoodWord,
                "the catalog knows \"brownie\" only as a kind of cookie, which Celiac Disease flags");
            (await OfferedAsync(db, await new HouseholdRestrictionResolver(db).ResolveAsync(8))).Should().Contain(new[] { pudding.Id, marie.Id, shake.Id, drink.Id }, "a diet of choice keeps today's reading");

            var counts = await DietGateStoreReports.CountsAsync(db);
            var celiac = counts.Types.Single(t => t.TypeId == CeliacRef && t.Reading == "all");
            celiac.RecipesUnknownWord.Should().BeGreaterThan(0);
            celiac.RecipesByFoodWord.Should().Be(1);
            celiac.RecipesUnknown.Should().Be(celiac.RecipesByTitle + celiac.RecipesByMethod + celiac.RecipesNoLine + celiac.RecipesUnknownFood + celiac.RecipesUnknownWord);

            var page = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);
            (await page.GetDietMatchesAsync(marie.Id, PersonId)).Should().ContainSingle(m => m.IngredientName == "Ingredient not understood");
            (await page.GetDietMatchesAsync(shake.Id, PersonId)).Should().ContainSingle(m => m.IngredientName == "Named by an ingredient");
        }

        [Fact]
        public async Task A_book_method_that_names_a_food_of_the_corpus_no_line_carries_holds_the_recipe_as_unknown()
        {
            using var db = NewContext();
            await SeedAsync(db, "Celiac Disease", CeliacRef);
            var ham = Curated("Ham", 10, "Pork, cured, ham, whole, separable lean only, roasted");
            db.Ingredients.Add(ham);
            await db.SaveChangesAsync();
            var poularde = Recipe("Poularde Valencienne", (ham.Id, "1 lb ham"));
            poularde.ScrapedAtUtc = DateTime.UtcNow;
            poularde.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Dish it, and surround it with a garnish of rizotto, combined with the ham." } };
            var plain = Recipe("Boiled Ham", (ham.Id, "1 lb ham"));
            plain.ScrapedAtUtc = DateTime.UtcNow;
            plain.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Boil the ham in a large pan and dish it on a hot plate." } };
            db.Recipes.AddRange(poularde, plain);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            var offered = await OfferedAsync(db, severe);
            offered.Should().NotContain(poularde.Id, "review ck16-2: the method names rizotto, a food of the corpus that no line carries (proof draw 2, 133979)");
            offered.Should().Contain(plain.Id, "a pan and a plate are no food");
            (await db.DietRecipeFlags.Where(f => f.RecipeId == poularde.Id && severe.Stored!.SetIds.Contains(f.SetId)).ToListAsync())
                .Should().Contain(f => f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.Method);
        }

        [Fact]
        public async Task The_dried_form_of_any_fruit_is_flagged_for_sulfites_whatever_food_the_line_is_on()
        {
            using var db = NewContext();
            await SeedAsync(db, "Sulfite Sensitivity", SulfiteRef);
            var currants = Curated("Currants", 9, "Currants, red and white, raw");
            var cranberries = Curated("Dried Cranberries", 9, "Cranberries, dried, sweetened");
            var oregano = Curated("Dried Oregano", 2, "Spices, oregano, dried");
            var flour = Curated("Rice Flour", 20, "Rice flour, white, unenriched");
            db.Ingredients.AddRange(currants, cranberries, oregano, flour);
            await db.SaveChangesAsync();
            var riceFlour = Recipe("Rice Flour No. 1", (flour.Id, "1 cup rice flour"), (currants.Id, "dried currants"));
            var cheeseBall = Recipe("Cranberry Pecan Cheese Ball", (cranberries.Id, "3/4 cup finely chopped dried cranberries"));
            var pizza = Recipe("Oregano Rice", (flour.Id, "1 cup rice flour"), (oregano.Id, "1 tsp dried oregano"));
            db.Recipes.AddRange(riceFlour, cheeseBall, pizza);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            var offered = await OfferedAsync(db, severe);
            offered.Should().NotContain(new[] { riceFlour.Id, cheeseBall.Id }, "dried currants and dried cranberries are dried fruit, read by kind (S3, S17), though the first is linked to fresh currants");
            offered.Should().Contain(pizza.Id, "dried oregano is no fruit");
            (await db.DietRecipeFlags.Where(f => f.RecipeId == riceFlour.Id && severe.Stored!.SetIds.Contains(f.SetId)).ToListAsync())
                .Should().Contain(f => f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.FoodWord && f.IngredientId == currants.Id);
        }

        [Fact]
        public async Task A_change_to_the_curated_foods_words_decides_every_recipe_again_in_the_changes_cycle()
        {
            using var db = NewContext();
            await SeedAsync(db, "Celiac Disease", CeliacRef);
            var rice = Curated("Rice", 20, "Rice, white, long-grain, raw");
            var spelt = Wording("spelt-free borecole");
            var borecole = Wording("2 lb borecole");
            db.Ingredients.AddRange(rice, spelt, borecole);
            await db.SaveChangesAsync();
            var kale = Recipe("Boiled Borecole", (borecole.Id, "2 lb borecole"), (rice.Id, "1 cup rice"));
            db.Recipes.Add(kale);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            (await OfferedAsync(db, severe)).Should().NotContain(kale.Id, "no curated food knows borecole");

            db.Ingredients.Add(Curated("Borecole", 11, "Kale, scotch, raw"));
            await db.SaveChangesAsync();
            var report = await builder.RunChangesAsync(db);
            report.RecipesDecided.Should().BeGreaterThan(0);
            severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            (await OfferedAsync(db, severe)).Should().Contain(kale.Id, "a newly curated plain food makes its word known, and the recipe on another food is decided again");
        }
    }
}
