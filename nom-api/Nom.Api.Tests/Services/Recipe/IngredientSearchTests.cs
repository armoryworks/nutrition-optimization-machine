using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The ingredient picker offers curated foods and the caller's own entries; pending
    /// imports and other users' import residue stay hidden, and an exact name match leads.
    /// Every word of the query must appear, in any order and singular or plural, and foods
    /// with nutrition data and plain forms rank above data-less rows and processed variants.
    /// </summary>
    public class IngredientSearchTests
    {
        private const long Me = 7;
        private const long Someone = 22;

        private static ApplicationDbContext NewContext()
        {
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            return new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                accessor);
        }

        private static RecipeOrchestrationService NewService(ApplicationDbContext db)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(c => c.PersonId).Returns(Me);
            return new(db,
                new HttpContextAccessor(),
                currentUser.Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object,
                new Mock<IRecipeNutritionService>().Object);
        }

        private static IngredientEntity Food(string name, CurationStatusEnum status, bool? wholeFood = null, long? createdBy = null) =>
            new() { Name = name, CurationStatusId = (long)status, IsWholeFood = wholeFood, CreatedByPersonId = createdBy };

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = NewContext();
            db.Ingredients.AddRange(
                Food("SALTED BUTTER, SALTED (Safeway, Inc.)", CurationStatusEnum.PendingCuration, true),
                Food("SALT & VINEGAR KETTLE CHIPS (Old Dutch Foods Inc)", CurationStatusEnum.PendingCuration, true),
                Food("Peanut butter, smooth style, with salt", CurationStatusEnum.Curated, true),
                Food("Salt", CurationStatusEnum.Curated),
                Food("yellow onion (diced", CurationStatusEnum.NonCurated, createdBy: Someone),
                Food("Yellow Onion", CurationStatusEnum.Curated),
                Food("Sumac", CurationStatusEnum.NonCurated, createdBy: Someone),
                Food("salt flakes (smoked", CurationStatusEnum.NonCurated, createdBy: Me));
            await db.SaveChangesAsync();
            return db;
        }

        [Fact]
        public async Task Search_PutsExactCuratedMatchFirst_AndHidesPendingImports()
        {
            await using var db = await SeedAsync();

            var names = (await NewService(db).SearchIngredientsAsync("salt")).Select(r => r.Name).ToList();

            names.First().Should().Be("Salt");
            names.Should().NotContain(n => n.Contains("Inc"));
            names.Should().Contain("Peanut butter, smooth style, with salt");
        }

        [Fact]
        public async Task Search_HidesOtherUsersResidue_ButKeepsTheCallersOwn()
        {
            await using var db = await SeedAsync();
            var service = NewService(db);

            (await service.SearchIngredientsAsync("yellow onion")).Select(r => r.Name)
                .Should().Equal("Yellow Onion");
            (await service.SearchIngredientsAsync("salt flakes")).Select(r => r.Name)
                .Should().Equal("salt flakes (smoked");
        }

        [Fact]
        public async Task Search_FindsAnotherUsersEntry_OnlyByItsExactName()
        {
            await using var db = await SeedAsync();
            var service = NewService(db);

            (await service.SearchIngredientsAsync("suma")).Should().BeEmpty();
            (await service.SearchIngredientsAsync("sumac")).Select(r => r.Name).Should().Equal("Sumac");
        }

        private static IngredientEntity WithNutrition(IngredientEntity food)
        {
            food.IngredientNutrients.Add(new IngredientNutrientEntity { Nutrient = new NutrientEntity { Name = "Protein " + food.Name }, Amount = 5m });
            return food;
        }

        private static async Task<ApplicationDbContext> SeedCatalogAsync()
        {
            var db = NewContext();
            var chickpeas = WithNutrition(Food("Chickpeas, mature seeds, canned, drained solids", CurationStatusEnum.Curated));
            chickpeas.Aliases.Add(new IngredientAliasEntity { AliasName = "garbanzo beans" });
            db.Ingredients.AddRange(
                Food("Black Beans", CurationStatusEnum.Curated, true),
                WithNutrition(Food("Beans, black, mature seeds, canned, low sodium", CurationStatusEnum.Curated)),
                WithNutrition(Food("Beans, black, mature seeds, raw", CurationStatusEnum.Curated)),
                WithNutrition(Food("Soup, black bean, canned, prepared with equal volume water", CurationStatusEnum.Curated)),
                WithNutrition(Food("Anchovies in olive oil", CurationStatusEnum.Curated)),
                WithNutrition(Food("Oil, olive, salad or cooking", CurationStatusEnum.Curated)),
                WithNutrition(Food("Potato, flesh and skin, raw", CurationStatusEnum.Curated)),
                chickpeas);
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<List<string>> Names(ApplicationDbContext db, string query) =>
            (await NewService(db).SearchIngredientsAsync(query)).Select(r => r.Name).ToList();

        [Fact]
        public async Task Search_MatchesEveryWordInAnyOrder_AndRanksFoodsWithDataFirst()
        {
            await using var db = await SeedCatalogAsync();

            var names = await Names(db, "black beans");

            names.Should().Contain("Black Beans");
            names.Take(2).Should().Equal(
                "Beans, black, mature seeds, raw",
                "Beans, black, mature seeds, canned, low sodium");
            names.IndexOf("Soup, black bean, canned, prepared with equal volume water")
                .Should().BeLessThan(names.IndexOf("Black Beans"), "a food with data outranks one without");
        }

        [Fact]
        public async Task Search_FindsUsdaCommaOrderFromNaturalPhrases()
        {
            await using var db = await SeedCatalogAsync();

            (await Names(db, "canned black")).Should().BeEquivalentTo(
                "Beans, black, mature seeds, canned, low sodium",
                "Soup, black bean, canned, prepared with equal volume water");
            (await Names(db, "potatoes")).Should().Equal("Potato, flesh and skin, raw");
        }

        [Fact]
        public async Task Search_OffersThePlainFoodBeforeOneThatMerelyContainsIt()
        {
            await using var db = await SeedCatalogAsync();

            (await Names(db, "olive oil")).Should().Equal("Oil, olive, salad or cooking", "Anchovies in olive oil");
        }

        [Fact]
        public async Task Search_MatchesAnAliasInAnyOrder_AndReportsIt()
        {
            await using var db = await SeedCatalogAsync();

            var result = (await NewService(db).SearchIngredientsAsync("beans garbanzo")).Single();

            result.Name.Should().Be("Chickpeas, mature seeds, canned, drained solids");
            result.MatchedAlias.Should().Be("garbanzo beans");
        }

        [Theory]
        [InlineData("Black Beans", new[] { "black", "bean" })]
        [InlineData("beans, black", new[] { "bean", "black" })]
        [InlineData("tomatoes", new[] { "tomato" })]
        [InlineData("cherries", new[] { "cherr" })]
        [InlineData("peaches", new[] { "peach" })]
        [InlineData("cream of tartar", new[] { "cream", "tartar" })]
        [InlineData("hummus", new[] { "hummus" })]
        [InlineData("glass", new[] { "glass" })]
        [InlineData("2% milk", new[] { "2", "milk" })]
        [InlineData("fries", new[] { "frie" })]
        public void Stems_DropPluralsStopWordsAndPunctuation(string query, string[] expected)
        {
            IngredientSearchTerms.Stems(query).Should().Equal(expected);
        }
    }
}
