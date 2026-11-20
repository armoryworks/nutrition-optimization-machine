using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The verified wording table as the first gate: an import lands a judged wording on its food
    /// before any name or alias is tried, and the link lane asks neither a name rule nor a model
    /// about a wording the table has a verdict on. Wordings are staging's top unresolved ones.
    /// </summary>
    public class VerifiedWordingGateTests
    {
        private const long Importer = 7;

        private sealed class CountingMatcher : IIngredientLinkMatcher
        {
            public List<string> Asked { get; } = new();
            public bool IsConfigured => true;
            public string ModelName => "fake:1b";
            public int PromptVersion => 1;

            public Task<IReadOnlyList<LinkAnswer>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default)
            {
                Asked.AddRange(questions.Select(q => q.Name));
                return Task.FromResult<IReadOnlyList<LinkAnswer>>(questions.Select(q => new LinkAnswer(q.IngredientId, null, 0m)).ToList());
            }
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static IConfiguration Settings(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

        private static IngredientLinkService Linker(ApplicationDbContext db, IIngredientLinkMatcher matcher, IConfiguration? configuration = null) =>
            new(db, matcher, new Mock<IIngredientLinkVerifier>().Object, new MemoryCache(new MemoryCacheOptions()),
                NullLogger<IngredientLinkService>.Instance, configuration);

        private static RecipeScrapingService Scraper(ApplicationDbContext db, IConfiguration? configuration)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(Importer);
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            return new RecipeScrapingService(
                db, currentUser.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance, configuration);
        }

        private static IConfiguration ResolveOnImport => Settings((RecipeScrapingService.ResolveOnImportSetting, "true"));

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, CurationStatusEnum status = CurationStatusEnum.Curated)
        {
            var food = new IngredientEntity
            {
                Name = name,
                CurationStatusId = (long)status,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static async Task<IngredientEntity> UsedAsync(ApplicationDbContext db, string name)
        {
            var ingredient = await FoodAsync(db, name, CurationStatusEnum.NonCurated);
            db.Recipes.Add(new RecipeEntity
            {
                Name = $"{name} recipe",
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = ingredient.Id, Quantity = 1, MeasurementId = 1, RawLine = name } },
            });
            await db.SaveChangesAsync();
            return ingredient;
        }

        private static async Task JudgeAsync(ApplicationDbContext db, string wording, string verdict, IngredientEntity? food = null)
        {
            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = wording, Verdict = verdict, IngredientId = food?.Id, Batch = "w01", Source = "verified:w01" });
            await db.SaveChangesAsync();
        }

        private static async Task<IngredientEntity> ImportAsync(ApplicationDbContext db, string ingredientName, IConfiguration? configuration)
        {
            var recipeName = $"Recipe {Guid.NewGuid():N}";
            var result = await Scraper(db, configuration).ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes =
                {
                    new ScraperRecipe
                    {
                        Name = recipeName,
                        Ingredients = { new ScraperIngredient { Name = ingredientName, RawLine = $"1 tsp {ingredientName}" } },
                    },
                },
            });

            result.Imported.Should().Be(1);
            var line = await db.RecipeIngredients.SingleAsync(ri => ri.Recipe.Name == recipeName);
            return await db.Ingredients.SingleAsync(i => i.Id == line.IngredientId);
        }

        [Fact]
        public async Task The_link_lane_never_takes_a_wording_the_table_has_a_verdict_on_whatever_the_verdict()
        {
            using var db = NewContext();
            var extract = await FoodAsync(db, "Vanilla Extract");
            await UsedAsync(db, "vanilla");
            await UsedAsync(db, "white wine");
            await UsedAsync(db, "kosher salt and pepper");
            await UsedAsync(db, "dragon fruit");
            await JudgeAsync(db, "vanilla", IngredientWordingVerdict.Food, extract);
            await JudgeAsync(db, "white wine", IngredientWordingVerdict.None);
            await JudgeAsync(db, "kosher salt and pepper", IngredientWordingVerdict.Unclear);

            var gated = await Linker(db, new CountingMatcher()).NextSourcesAsync(10);
            var ungated = await Linker(db, new CountingMatcher(), Settings((IngredientLinkService.GateLinkLaneSetting, "false"))).NextSourcesAsync(10);

            gated.Select(s => s.Name).Should().Equal("dragon fruit");
            ungated.Select(s => s.Name).Should().BeEquivalentTo("vanilla", "white wine", "kosher salt and pepper", "dragon fruit");
        }

        [Fact]
        public async Task A_judged_wording_handed_to_the_lane_gets_no_proposal_and_no_question_before_any_rule_runs()
        {
            using var db = NewContext();
            var extract = await FoodAsync(db, "Vanilla Extract");
            var vanilla = await UsedAsync(db, "vanilla");
            var wine = await UsedAsync(db, "white wine");
            var fruit = await UsedAsync(db, "dragon fruit");
            await JudgeAsync(db, "vanilla", IngredientWordingVerdict.Food, extract);
            await JudgeAsync(db, "white wine", IngredientWordingVerdict.None);
            var matcher = new CountingMatcher();

            await Linker(db, matcher).ProposeAsync(new[]
            {
                new LinkSource(vanilla.Id, vanilla.Name, 1),
                new LinkSource(wine.Id, wine.Name, 1),
                new LinkSource(fruit.Id, fruit.Name, 1),
            });

            matcher.Asked.Should().BeEmpty();
            (await db.FoodCatalogProposals.Select(p => p.IngredientId).ToListAsync()).Should().Equal(fruit.Id);
        }

        [Fact]
        public async Task An_imported_verified_wording_lands_on_its_food_before_an_uncurated_ingredient_of_that_name()
        {
            using var db = NewContext();
            var extract = await FoodAsync(db, "Vanilla Extract");
            await FoodAsync(db, "vanilla", CurationStatusEnum.NonCurated);
            await JudgeAsync(db, "vanilla", IngredientWordingVerdict.Food, extract);
            var before = await db.Ingredients.CountAsync();

            var landed = await ImportAsync(db, "Vanilla", ResolveOnImport);

            landed.Id.Should().Be(extract.Id);
            (await db.Ingredients.CountAsync()).Should().Be(before);
        }

        [Fact]
        public async Task An_import_falls_back_to_the_name_when_the_wording_names_no_single_food_its_food_lost_curation_or_the_setting_is_off()
        {
            using var db = NewContext();
            var extract = await FoodAsync(db, "Vanilla Extract");
            var peel = await FoodAsync(db, "Lemon Peel", CurationStatusEnum.PendingCuration);
            await JudgeAsync(db, "vanilla", IngredientWordingVerdict.Food, extract);
            await JudgeAsync(db, "lemon rind", IngredientWordingVerdict.Food, peel);
            await JudgeAsync(db, "kosher salt and pepper", IngredientWordingVerdict.Unclear);

            var unclear = await ImportAsync(db, "kosher salt and pepper", ResolveOnImport);
            var uncurated = await ImportAsync(db, "lemon rind", ResolveOnImport);
            var off = await ImportAsync(db, "vanilla", null);

            unclear.Should().Match<IngredientEntity>(i => i.Name == "kosher salt and pepper" && i.CurationStatusId == (long)CurationStatusEnum.NonCurated);
            uncurated.Should().Match<IngredientEntity>(i => i.Name == "lemon rind" && i.Id != peel.Id);
            off.Should().Match<IngredientEntity>(i => i.Name == "vanilla" && i.Id != extract.Id);
        }
    }
}
