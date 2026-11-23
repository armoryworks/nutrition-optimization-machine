using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The surfaces left after the source rights rule that could still show a facts-only recipe's publisher text, each
    /// walked with a facts-only recipe and the same recipe of another class, so an empty answer cannot pass:
    /// <list type="bullet">
    /// <item><c>GET api/Recipe/{id}/diet</c> and the meal plan's severe-diet conflicts (<c>GetSevereDietConflictsAsync</c>):
    /// a warning names a line by NOM's own wording (amount, unit, catalog food), never the line as written.</item>
    /// <item><c>GET api/Recipe/{id}</c>, its <c>alcoholNote</c>: the drinks, flavourings and lines that cannot be counted, the same way.</item>
    /// <item><c>POST api/RecipeScraping/test-scrape-url</c> and <c>POST api/RecipeImport/test-scrape-url</c>: the test scrape of a
    /// facts-only source serves the reduced title, times and parsed amounts and foods, no description, method, line notes or photo.</item>
    /// <item><c>POST api/SmartShoppingList/optimize</c> and the grocery export (<c>POST api/GroceryExport/lists/{id}</c>): the
    /// "From recipe" note of an item names the recipe as it is now, not a stored copy of a distinctive title.</item>
    /// <item><c>GET api/MealPlan</c>, <c>GET api/MealPlan/{id}</c> and the week view (<c>GET api/MealPlan/week</c>): an entry of a
    /// facts-only recipe takes the recipe's current name, not the title it stored when it was planned.</item>
    /// <item>Every surface that names a line by its food (the warnings and the note above, a list a recipe is added to, the test
    /// scrape): a facts-only line on a food created from the line's own text (not curated, no FDC id) is named by its own food
    /// words only (<c>ProseWithholding.FoodWords</c>), never by that food's name; the detail, search, smart shopping list and bulk
    /// export the same way (<c>SourceRightsServiceTests</c>).</item>
    /// <item><c>GET api/Recipe/dashboard/analytics</c>, its <c>mostUsedIngredients</c>: the foods of the author's lines, a facts-only line's the same way.</item>
    /// <item><c>POST api/SmartShoppingList/optimize</c> and generate: items named by no food words ("Ingredient") are never added together,
    /// as they may be different foods.</item>
    /// </list>
    /// The detail's line notes, search, popular, recent, the smart shopping list's lines and the bulk export and print are walked by
    /// <c>SourceRightsServiceTests</c>; description and steps are withheld (<c>ProseWithheldEndpointTests</c>). Admin pages that
    /// show line text (curation, reprocess, compound and unfold lines, the source pages) answer only <c>CanManageCuration</c>.
    /// </summary>
    public class FactsOnlySurfaceTests
    {
        private const long Gram = 1, Cup = 2, Alcohol = 5038, HouseholdId = 7, PersonId = 70, NoAlcoholRef = 60913;
        private const string PublisherLine = "1/4 cup brandy, Grandma's secret";

        private sealed class NoEncryption : IDataEncryptionService
        {
            public string EncryptString(string plainText) => plainText;
            public string DecryptString(string cipherText) => cipherText;
            public byte[] EncryptBytes(byte[] data) => data;
            public byte[] DecryptBytes(byte[] data) => data;
            public Task<byte[]> EncryptFileAsync(byte[] data) => Task.FromResult(data);
            public Task<byte[]> DecryptFileAsync(byte[] data) => Task.FromResult(data);
            public static string GenerateEncryptionKey() => "key";
            public static string GenerateInitializationVector() => "iv";
            public bool IsEncrypted(string value) => false;
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private sealed record Seeded(long FactsOnlyId, long OtherId, long VanillaId);

        private static async Task<Seeded> SeedAsync(ApplicationDbContext db)
        {
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 236.6m });
            db.Nutrients.Add(new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = Gram });
            await db.SaveChangesAsync();
            var vanilla = new IngredientEntity { Name = "Vanilla Extract", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 2 };
            vanilla.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = 34.4m, MeasurementId = Gram });
            db.Ingredients.Add(vanilla);
            await db.SaveChangesAsync();

            RecipeEntity Recipe(string name, string? rights) => new()
            {
                Name = name, AuthorId = PersonId, Servings = 12, CurationStatusId = (long)CurationStatusEnum.NonCurated, SourceRights = rights,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = vanilla.Id, Quantity = 0.25m, MeasurementId = Cup, RawLine = PublisherLine, SortOrder = 0 } },
            };

            var factsOnly = Recipe("Brandy Cream", SourceRightsRule.FactsOnly);
            var other = Recipe("Brandy Cream (open)", SourceRightsRule.Clear);
            db.Recipes.AddRange(factsOnly, other);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Seeded(factsOnly.Id, other.Id, vanilla.Id);
        }

        private static async Task RestrictAsync(ApplicationDbContext db)
        {
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
        }

        private static RecipeOrchestrationService Recipes(ApplicationDbContext db) =>
            new(db, new HttpContextAccessor(), Mock.Of<ICurrentUserService>(), Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>());

        [Fact]
        public async Task Diet_warnings_name_a_facts_only_line_by_nom_s_wording_on_the_recipe_and_in_the_meal_plan()
        {
            await using var db = NewContext();
            var seeded = await SeedAsync(db);
            await RestrictAsync(db);
            var service = Recipes(db);

            (await service.GetDietMatchesAsync(seeded.FactsOnlyId, PersonId)).Should().ContainSingle()
                .Which.IngredientName.Should().Be("0.25 cup Vanilla Extract");
            var conflicts = await service.GetSevereDietConflictsAsync(new[] { seeded.FactsOnlyId, seeded.OtherId }, PersonId);
            JsonSerializer.Serialize(conflicts.GetValueOrDefault(seeded.FactsOnlyId)).Should().NotContain("Grandma");
            (await service.GetDietMatchesAsync(seeded.OtherId, PersonId)).Should().ContainSingle()
                .Which.IngredientName.Should().Be(PublisherLine, "a recipe of a source that is not facts-only keeps its line as written");
        }

        [Fact]
        public async Task The_alcohol_note_of_a_facts_only_recipe_names_its_lines_by_nom_s_wording()
        {
            await using var db = NewContext();
            var seeded = await SeedAsync(db);
            async Task<RecipeAlcoholNoteModel?> Note(long id)
            {
                var recipe = await db.Recipes.AsNoTracking().Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                    .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Measurement).SingleAsync(r => r.Id == id);
                return RecipeNutritionService.AlcoholNote(recipe, new Dictionary<long, decimal> { [seeded.VanillaId] = 34.4m });
            }

            var factsOnly = await Note(seeded.FactsOnlyId);
            factsOnly!.Uncounted.Should().Equal("0.25 cup Vanilla Extract");
            JsonSerializer.Serialize(factsOnly).Should().NotContain("Grandma");
            (await Note(seeded.OtherId))!.Uncounted.Should().Equal(PublisherLine);
        }

        [Fact]
        public async Task The_test_scrape_of_a_facts_only_source_serves_facts_only_on_both_routes()
        {
            await using var db = NewContext();
            db.ScrapingSources.Add(new ScrapingSourceEntity
            {
                Domain = "facts.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = DateTime.UtcNow,
                RightsClass = SourceRightsRule.FactsOnly, FactsOnlyApprovedAt = DateTime.UtcNow,
            });
            db.ScrapingSources.Add(new ScrapingSourceEntity
            {
                Domain = "open.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = DateTime.UtcNow, RightsClass = SourceRightsRule.Clear,
            });
            await db.SaveChangesAsync();
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            scraper.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string url, CancellationToken _) => new ScraperResult
            {
                Success = true,
                Recipe = new ScraperRecipe
                {
                    Name = "The Best Chili You'll Ever Make", Description = "PUBLISHERPROSE description", ImageUrl = "https://img.example/chili.jpg", SourceUrl = url, PrepTime = "PT10M",
                    Ingredients = { new ScraperIngredient { RawLine = "1 pound ground beef, spooned and leveled", Name = "ground beef", Quantity = 1, Unit = "pound", Notes = "PUBLISHERPROSE note" } },
                    Steps = { new ScraperStep { Order = 1, Instruction = "PUBLISHERPROSE step" } },
                },
            });
            var sources = new Mock<IScrapingSourceService>();
            sources.Setup(s => s.GetDomainStatusAsync(It.IsAny<string>())).ReturnsAsync(ScrapingSourceStatusEnum.Approved);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.RightsGateSetting] = "true" }).Build();
            var scraping = new RecipeScrapingService(db, Mock.Of<ICurrentUserService>(), scraper.Object, sources.Object, new RecipeVettingService(), NullLogger<RecipeScrapingService>.Instance, config);

            var facts = await scraping.TestScrapeRecipeAsync(new RecipeScrapingTestRequestModel { Url = "https://facts.example/chili/" });
            facts.Name.Should().Be("Chili");
            JsonSerializer.Serialize(facts).Should().NotContain("PUBLISHERPROSE").And.NotContain("img.example").And.Contain("ground beef");
            facts.PrepTime.Should().Be("PT10M");
            JsonSerializer.Serialize(await scraping.TestScrapeRecipeAsync(new RecipeScrapingTestRequestModel { Url = "https://open.example/chili/" })).Should().Contain("PUBLISHERPROSE");

            var import = new RecipeImportOrchestrationService(db, NullLogger<RecipeImportOrchestrationService>.Instance, scraping, Mock.Of<ITesseractOcrService>());
            JsonSerializer.Serialize(await import.TestUrlScrapingAsync("https://facts.example/chili/")).Should().NotContain("PUBLISHERPROSE");
        }

        [Fact]
        public async Task A_facts_only_line_on_a_food_named_from_its_own_text_is_named_by_its_food_words_in_warnings_notes_lists_and_the_test_scrape()
        {
            await using var db = NewContext();
            var seeded = await SeedAsync(db);
            const string rum = "1/4 cup dark rum, or more if you are feeling festive!";
            const string sentence = "Tip-you can swap the oats for rice flakes and add raisins if you like!";
            var rumFood = new IngredientEntity { Name = rum, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var sentenceFood = new IngredientEntity { Name = sentence, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(rumFood, sentenceFood);
            await db.SaveChangesAsync();
            foreach (var id in new[] { seeded.FactsOnlyId, seeded.OtherId })
            {
                db.RecipeIngredients.AddRange(
                    new RecipeIngredientEntity { RecipeId = id, IngredientId = rumFood.Id, Quantity = 0.25m, MeasurementId = Cup, RawLine = rum, SortOrder = 1 },
                    new RecipeIngredientEntity { RecipeId = id, IngredientId = sentenceFood.Id, Quantity = 0m, MeasurementId = Gram, RawLine = sentence, SortOrder = 2 });
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            async Task<RecipeAlcoholNoteModel?> Note(long id)
            {
                var recipe = await db.Recipes.AsNoTracking().Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                    .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Measurement).SingleAsync(r => r.Id == id);
                return RecipeNutritionService.AlcoholNote(recipe, new Dictionary<long, decimal> { [seeded.VanillaId] = 34.4m, [rumFood.Id] = 33.4m });
            }

            var note = JsonSerializer.Serialize(await Note(seeded.FactsOnlyId));
            note.Should().Contain("0.25 cup dark rum").And.NotContain("festive");
            JsonSerializer.Serialize(await Note(seeded.OtherId)).Should().Contain("festive", "a recipe of a source that is not facts-only keeps its line as written");

            await RestrictAsync(db);
            var service = Recipes(db);
            JsonSerializer.Serialize(await service.GetDietMatchesAsync(seeded.FactsOnlyId, PersonId)).Should().NotContain("festive").And.NotContain("rice flakes");
            JsonSerializer.Serialize(await service.GetSevereDietConflictsAsync(new[] { seeded.FactsOnlyId }, PersonId)).Should().NotContain("festive");

            db.ShoppingLists.Add(new ShoppingListEntity { Id = 2, Name = "Week", AuthorId = PersonId });
            await db.SaveChangesAsync();
            await new ShoppingListOrchestrationService(db).AddRecipeIngredientsAsync(new ShoppingListRecipeAddModel { ShoppingListId = 2, RecipeId = seeded.FactsOnlyId }, PersonId);
            (await db.ShoppingListItems.AsNoTracking().Where(i => i.ShoppingListId == 2).Select(i => i.Name).ToListAsync())
                .Should().BeEquivalentTo(new[] { "Vanilla Extract", "dark rum", ProseWithholding.UnnamedFood });

            db.ScrapingSources.Add(new ScrapingSourceEntity
            {
                Domain = "facts.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = DateTime.UtcNow,
                RightsClass = SourceRightsRule.FactsOnly, FactsOnlyApprovedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            scraper.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string url, CancellationToken _) => new ScraperResult
            {
                Success = true,
                Recipe = new ScraperRecipe
                {
                    Name = "Nests", SourceUrl = url,
                    Ingredients =
                    {
                        new ScraperIngredient { RawLine = sentence, Name = sentence },
                        new ScraperIngredient { RawLine = "2 cups rolled oats", Name = "rolled oats", Quantity = 2, Unit = "cup" },
                    },
                },
            });
            var sources = new Mock<IScrapingSourceService>();
            sources.Setup(s => s.GetDomainStatusAsync(It.IsAny<string>())).ReturnsAsync(ScrapingSourceStatusEnum.Approved);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.RightsGateSetting] = "true" }).Build();
            var scraping = new RecipeScrapingService(db, Mock.Of<ICurrentUserService>(), scraper.Object, sources.Object, new RecipeVettingService(), NullLogger<RecipeScrapingService>.Instance, config);
            var preview = await scraping.TestScrapeRecipeAsync(new RecipeScrapingTestRequestModel { Url = "https://facts.example/nests/" });
            preview.Ingredients.Select(i => i.Name).Should().Equal(ProseWithholding.UnnamedFood, "rolled oats");
        }

        [Fact]
        public async Task The_author_dashboard_names_a_facts_only_line_s_food_by_its_food_words_only()
        {
            await using var db = NewContext();
            var seeded = await SeedAsync(db);
            const string rum = "1/4 cup dark rum, or more if you are feeling festive!";
            const string sentence = "Tip-you can swap the oats for rice flakes and add raisins if you like!";
            var rumFood = new IngredientEntity { Name = rum, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var sentenceFood = new IngredientEntity { Name = sentence, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(rumFood, sentenceFood);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Pending" });
            await db.SaveChangesAsync();
            db.RecipeIngredients.AddRange(
                new RecipeIngredientEntity { RecipeId = seeded.FactsOnlyId, IngredientId = rumFood.Id, Quantity = 0.25m, MeasurementId = Cup, RawLine = rum, SortOrder = 1 },
                new RecipeIngredientEntity { RecipeId = seeded.FactsOnlyId, IngredientId = sentenceFood.Id, Quantity = 0m, MeasurementId = Gram, RawLine = sentence, SortOrder = 2 });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var used = (await Recipes(db).GetDashboardAnalyticsAsync(PersonId)).MostUsedIngredients;

            used.Select(i => i.Name).Should().BeEquivalentTo(new[] { "Vanilla Extract", "dark rum", ProseWithholding.UnnamedFood });
            JsonSerializer.Serialize(used).Should().NotContain("festive").And.NotContain("raisins");

            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = seeded.OtherId, IngredientId = rumFood.Id, Quantity = 0.25m, MeasurementId = Cup, RawLine = rum, SortOrder = 1 });
            await db.SaveChangesAsync();
            (await Recipes(db).GetDashboardAnalyticsAsync(PersonId)).MostUsedIngredients.Select(i => i.Name).Should().Contain(rum,
                "a line of a recipe of another class keeps its food's name");
        }

        [Fact]
        public async Task Smart_shopping_list_items_with_no_food_words_are_never_added_together()
        {
            await using var db = NewContext();
            db.ShoppingLists.Add(new ShoppingListEntity
            {
                Id = 3, Name = "Week", AuthorId = PersonId,
                Items = new List<ShoppingListItemEntity>
                {
                    new() { Name = ProseWithholding.UnnamedFood, Quantity = 1m },
                    new() { Name = ProseWithholding.UnnamedFood, Quantity = 1m },
                    new() { Name = "Eggs", Quantity = 2m },
                    new() { Name = "Eggs", Quantity = 4m },
                },
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var smart = new SmartShoppingListService(db, new HttpContextAccessor(), Mock.Of<ICurrentUserService>(), NullLogger<SmartShoppingListService>.Instance, new System.Net.Http.HttpClient());
            var optimized = await smart.OptimizeShoppingListAsync(new ShoppingListOptimizationModel { ShoppingListId = 3 });

            optimized.Items.Select(i => (i.Name, i.Quantity)).Should().BeEquivalentTo(new[]
            {
                (ProseWithholding.UnnamedFood, 1m), (ProseWithholding.UnnamedFood, 1m), ("Eggs", 6m),
            }, "two lines named by no food words may be two different foods, so neither is lost to the other");
        }

        [Fact]
        public async Task A_shopping_item_note_and_a_planned_entry_name_a_facts_only_recipe_as_it_is_now()
        {
            await using var db = NewContext();
            var seeded = await SeedAsync(db);
            const string stored = "The Best Brandy Cream You'll Ever Have";
            db.ShoppingLists.Add(new ShoppingListEntity
            {
                Id = 1, Name = "Week", AuthorId = PersonId,
                Items = new List<ShoppingListItemEntity>
                {
                    new() { Name = "Vanilla Extract", RecipeId = seeded.FactsOnlyId, Note = ShoppingItemNote.ForRecipe(stored) },
                    new() { Name = "Eggs", Note = "the brown ones" },
                },
            });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = 500, Name = "Dinner" });
            db.MealPlans.AddRange(
                new MealPlanEntity { HouseholdId = HouseholdId, AuthorId = PersonId, Date = new DateOnly(2026, 11, 23), MealTypeId = 500, RecipeId = seeded.FactsOnlyId, Title = stored },
                new MealPlanEntity { HouseholdId = HouseholdId, AuthorId = PersonId, Date = new DateOnly(2026, 11, 24), MealTypeId = 500, RecipeId = seeded.OtherId, Title = "Our own name for it" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var smart = new SmartShoppingListService(db, new HttpContextAccessor(), Mock.Of<ICurrentUserService>(), NullLogger<SmartShoppingListService>.Instance, new System.Net.Http.HttpClient());
            var optimized = await smart.OptimizeShoppingListAsync(new ShoppingListOptimizationModel { ShoppingListId = 1 });
            JsonSerializer.Serialize(optimized).Should().NotContain("Ever Have").And.Contain("From recipe: Brandy Cream").And.Contain("the brown ones");

            GroceryExportRequest? sent = null;
            var client = new Mock<IGroceryExportClient>();
            client.Setup(c => c.ExportAsync(It.IsAny<GroceryExportRequest>(), It.IsAny<CancellationToken>()))
                .Callback((GroceryExportRequest r, CancellationToken _) => sent = r)
                .ReturnsAsync(new GroceryExportResult { Success = true });
            var grocery = new GroceryExportOrchestrationService(db, client.Object, Mock.Of<IRetailPackagingOrchestrationService>(), new NoEncryption(), NullLogger<GroceryExportOrchestrationService>.Instance);
            await grocery.ExportListAsync(1, PersonId, new GroceryExportOptionsModel { Provider = "text" });
            sent.Should().NotBeNull();
            sent!.Items.Select(i => i.Note).Should().Contain("From recipe: Brandy Cream").And.NotContain(n => n != null && n.Contains("Ever Have"));

            var plans = new MealPlanOrchestrationService(db, Mock.Of<IPolicyEnforcementService>());
            var all = await plans.GetAllMealPlansAsync();
            all.Select(p => p.Title).Should().BeEquivalentTo(new[] { "Brandy Cream", "Our own name for it" });
            (await plans.GetMealPlanAsync(all.Single(p => p.RecipeId == seeded.FactsOnlyId).Id))!.Title.Should().Be("Brandy Cream");
        }
    }
}
