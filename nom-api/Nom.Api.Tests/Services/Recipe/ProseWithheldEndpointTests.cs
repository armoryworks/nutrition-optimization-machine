using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// A recipe whose prose is withheld stays listed and usable, but no endpoint that serves a
    /// recipe's description or steps serves its stored text, to anyone, author included: detail,
    /// lists, search, suggestions, plans, cookbooks, the curation queue, bulk export (JSON, CSV and
    /// the PDF print) and the image finder's work items. A shown recipe walked alongside proves each
    /// endpoint does serve prose, so an empty answer cannot pass. The detail serves rule-built key
    /// facts and the source link instead.
    /// </summary>
    public class ProseWithheldEndpointTests
    {
        private const long Author = 5, Harvester = 99, Withheld = 100, Shown = 101, PendingWithheld = 102, PendingShown = 103, Dinner = 700;
        private const string WithheldText = "WITHHELDPROSE";
        private const string ShownText = "SHOWNPROSE";

        private const string Source = """
            {"@type":"Recipe","name":"Withheld pie","description":"A pie.",
             "recipeIngredient":["2 cups flour","1 cup sugar"],
             "recipeInstructions":[
               {"@type":"HowToStep","text":"Preheat the oven to 350 degrees F (175 degrees C)."},
               {"@type":"HowToStep","text":"Spread the batter in a greased 9 x 13-inch pan and bake for 30 to 40 minutes."},
               {"@type":"HowToStep","text":"Let it cool for 30 minutes before slicing."}]}
            """;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Recipe(long id, string name, bool withheld, CurationStatusEnum status) => new()
        {
            Id = id,
            Name = name,
            Description = $"{(withheld ? WithheldText : ShownText)} description of {name}",
            AuthorId = Author,
            Visibility = RecipeVisibilityEnum.Public,
            CurationStatusId = (long)status,
            ScrapedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            LicenseStatus = RecipeLicenseStatus.Unknown,
            SourceUrl = $"https://example.com/{id}",
            ProseWithheld = withheld,
            Ratings = new List<RecipeRatingEntity> { new() { RaterId = Author, Rating = 4 } },
            RecipeSteps = new List<RecipeStepEntity>
            {
                new() { StepNumber = 1, Summary = "one", Description = $"{(withheld ? WithheldText : ShownText)} step one of {name}" },
                new() { StepNumber = 2, Summary = "two", Description = $"{(withheld ? WithheldText : ShownText)} step two of {name}" },
            },
        };

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = NewContext();
            db.Persons.AddRange(new PersonEntity { Id = Author, Name = "Author", UserId = "u-author" }, new PersonEntity { Id = Harvester, Name = "Harvester" });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.PendingCuration, Name = "Pending" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" },
                new ReferenceEntity { Id = Dinner, Name = "Dinner" });
            db.Recipes.AddRange(
                Recipe(Withheld, "Withheld pie", true, CurationStatusEnum.Curated),
                Recipe(Shown, "Shown pie", false, CurationStatusEnum.Curated),
                Recipe(PendingWithheld, "Pending withheld pie", true, CurationStatusEnum.PendingCuration),
                Recipe(PendingShown, "Pending shown pie", false, CurationStatusEnum.PendingCuration));
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = Withheld, RawJsonLd = Source });
            db.Plans.Add(new PlanEntity
            {
                Id = 1, Name = "Week", AuthorId = Author, CurationStatusId = (long)CurationStatusEnum.Curated,
                Meals = new List<MealEntity> { new() { MealTypeId = Dinner, Date = new DateOnly(2026, 11, 20) } },
            });
            db.HouseholdCookbooks.Add(new HouseholdCookbookEntity { Id = 1, HouseholdId = 1, Name = "Pies" });
            await db.SaveChangesAsync();
            var meal = await db.Meals.Include(m => m.Recipes).SingleAsync();
            meal.Recipes = await db.Recipes.Where(r => r.Id == Withheld || r.Id == Shown).ToListAsync();
            db.HouseholdCookbookRecipes.AddRange(
                new HouseholdCookbookRecipeEntity { HouseholdCookbookId = 1, RecipeId = Withheld },
                new HouseholdCookbookRecipeEntity { HouseholdCookbookId = 1, RecipeId = Shown });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static RecipeOrchestrationService Recipes(ApplicationDbContext db) =>
            new(db, new HttpContextAccessor(), Mock.Of<ICurrentUserService>(), Mock.Of<Nom.Orch.UtilityInterfaces.IMediaStorageService>(), Mock.Of<IRecipeNutritionService>());

        private static string Json(object? value) => JsonSerializer.Serialize(value);

        private static void ServesNoWithheldProse(string served, bool shownToo = true)
        {
            served.Should().NotContain(WithheldText);
            if (shownToo) served.Should().Contain(ShownText, "the endpoint must serve the shown recipe's prose, or the walk proves nothing");
        }

        [Fact]
        public async Task The_detail_serves_key_facts_and_the_source_link_in_place_of_the_method_to_every_reader()
        {
            await using var db = await SeedAsync();
            foreach (var reader in new long?[] { null, Author })
            {
                var detail = await Recipes(db).GetRecipeAsync(Withheld, reader);

                detail.Should().NotBeNull();
                detail!.ProseWithheld.Should().BeTrue();
                detail.Description.Should().BeEmpty();
                detail.Steps.Should().BeEmpty();
                detail.SourceUrl.Should().Be($"https://example.com/{Withheld}");
                detail.KeyFacts.Should().Equal("oven 350°F", "9 x 13-inch pan", "bake 30 to 40 min", "cool 30 min");
                Json(detail).Should().NotContain(WithheldText);
            }

            var shown = await Recipes(db).GetRecipeAsync(Shown, null);
            shown!.ProseWithheld.Should().BeFalse();
            shown.KeyFacts.Should().BeNull();
            Json(shown).Should().Contain(ShownText);
        }

        [Fact]
        public async Task Recipe_lists_serve_no_withheld_prose()
        {
            await using var db = await SeedAsync();
            ServesNoWithheldProse(Json(await Recipes(db).GetAllRecipesAsync(null)));
            ServesNoWithheldProse(Json(await Recipes(db).GetMyRecipesAsync(Author)));
        }

        [Fact]
        public async Task Search_and_its_shortcuts_keep_the_recipe_and_serve_no_withheld_prose()
        {
            await using var db = await SeedAsync();
            var search = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);

            var found = await search.SearchRecipesAsync(new RecipeSearchModel { Query = "pie", IncludeSteps = true, IncludeIngredients = false });
            found.Results.Select(r => (long)r.Id).Should().Contain(Withheld, "a withheld recipe stays in search");
            ServesNoWithheldProse(Json(found));
            ServesNoWithheldProse(Json(await search.GetPopularRecipesAsync(10)));
            ServesNoWithheldProse(Json(await search.GetRecentRecipesAsync(10)));
        }

        [Fact]
        public async Task Suggestions_serve_no_withheld_prose()
        {
            await using var db = await SeedAsync();
            var suggestions = new RecipeSuggestionService(NullLogger<RecipeSuggestionService>.Instance, db, new HttpClient());

            ServesNoWithheldProse(Json(await suggestions.GetPopularRecipeSuggestionsAsync(new RecipeSuggestionQueryModel())));
            ServesNoWithheldProse(Json(await suggestions.GetRecentRecipeSuggestionsAsync(new RecipeSuggestionQueryModel())));
            ServesNoWithheldProse(Json(await suggestions.GetTrendingRecipesAsync(10)), shownToo: false);
        }

        [Fact]
        public async Task Plans_and_cookbooks_keep_the_recipe_and_serve_no_withheld_prose()
        {
            await using var db = await SeedAsync();
            var plans = await new PlanOrchestrationService(db, NullLogger<PlanOrchestrationService>.Instance).GetMyPlansAsync(Author);
            plans.SelectMany(p => p.Meals).SelectMany(m => m.Recipes).Select(r => r.Id).Should().Contain(Withheld);
            ServesNoWithheldProse(Json(plans));

            var cookbook = await new CookbookOrchestrationService(db, NullLogger<CookbookOrchestrationService>.Instance).GetCookbookRecipesAsync(1);
            cookbook.Select(r => r.Id).Should().Contain(Withheld);
            ServesNoWithheldProse(Json(cookbook));
        }

        [Fact]
        public async Task The_curation_queue_serves_no_withheld_prose()
        {
            await using var db = await SeedAsync();
            var queue = await new CurationOrchestrationService(db, NullLogger<CurationOrchestrationService>.Instance).GetCurationQueueAsync();
            queue.Select(q => q.Id).Should().Contain(PendingWithheld);
            ServesNoWithheldProse(Json(queue));
        }

        [Fact]
        public async Task The_image_finder_work_items_carry_no_withheld_prose()
        {
            await using var db = await SeedAsync();
            var items = await new RecipeImageService(db, Mock.Of<Nom.Orch.UtilityInterfaces.IMediaStorageService>(), NullLogger<RecipeImageService>.Instance).GetMissingAsync(10);
            items.Select(i => i.Id).Should().Contain(Withheld);
            ServesNoWithheldProse(Json(items));
        }

        [Theory]
        [InlineData(ExportTypes.Json)]
        [InlineData(ExportTypes.Csv)]
        [InlineData(ExportTypes.Pdf)]
        public async Task Bulk_export_and_print_serve_no_withheld_prose(ExportTypes type)
        {
            await using var db = await SeedAsync();
            var service = new RecipeBulkOperationsService(db, Mock.Of<IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(), NullLogger<RecipeBulkOperationsService>.Instance);

            var result = await service.ExportRecipesAsync(new RecipeBulkExportModel { RecipeIds = new List<long> { Withheld, Shown }, RequesterPersonId = Author, ExportType = type });

            result.Success.Should().BeTrue(string.Join(";", result.Errors));
            var path = Path.Combine(Directory.GetCurrentDirectory(), "exports", $"recipes_export_{result.ExportId}.{type.ToString().ToLowerInvariant()}");
            try
            {
                var text = type == ExportTypes.Pdf
                    ? string.Join("\n", PdfDocument.Open(await File.ReadAllBytesAsync(path)).GetPages().Select(p => ContentOrderTextExtractor.GetText(p)))
                    : await File.ReadAllTextAsync(path);
                ServesNoWithheldProse(text);
                if (type != ExportTypes.Csv) text.Should().Contain("bake 30 to 40 min");
                if (type == ExportTypes.Pdf) text.Should().Contain($"Full method: https://example.com/{Withheld}");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task Saving_an_edit_of_a_withheld_recipe_keeps_its_stored_prose_until_a_person_writes_their_own()
        {
            await using var db = await SeedAsync();
            var service = Recipes(db);

            var saved = await service.UpdateRecipeAsync(Withheld, new UpdateRecipeRequest { Id = Withheld, Name = "Withheld pie, renamed" });
            db.ChangeTracker.Clear();

            Json(saved).Should().NotContain(WithheldText);
            var recipe = await db.Recipes.Include(r => r.RecipeSteps).SingleAsync(r => r.Id == Withheld);
            recipe.ProseWithheld.Should().BeTrue();
            recipe.Description.Should().StartWith(WithheldText, "a blank description from a form that never saw the text keeps it");
            recipe.RecipeSteps!.Should().HaveCount(2, "an empty step list from a form that never saw the steps keeps them");
            db.ChangeTracker.Clear();

            await service.UpdateRecipeAsync(Withheld, new UpdateRecipeRequest
            {
                Id = Withheld, Name = "Withheld pie", Description = "Our own pie.",
                Steps = new List<RecipeStepModel> { new() { Order = 1, Description = "Bake it our way." } },
            });
            db.ChangeTracker.Clear();

            recipe = await db.Recipes.Include(r => r.RecipeSteps).SingleAsync(r => r.Id == Withheld);
            recipe.ProseWithheld.Should().BeFalse("a person has replaced both the description and the steps");
            recipe.RecipeSteps!.Select(s => s.Description).Should().Equal("Bake it our way.");
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == ProseRecheckService.WithholdEndChangeType)).EntityId.Should().Be(Withheld);
        }
    }
}
