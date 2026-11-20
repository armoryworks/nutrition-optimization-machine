using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// A photo import must persist everything the OCR parsed. It used to keep only
    /// the title and description and drop ingredients, steps, times and yield on
    /// the floor while reporting success.
    /// </summary>
    public class OcrRecipeImportTests
    {
        private const long AuthorId = 7L;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static RecipeImportOrchestrationService NewService(ApplicationDbContext db, OcrRecipeData ocr)
        {
            var ocrService = new Mock<ITesseractOcrService>();
            ocrService.Setup(s => s.ProcessImageWithOcrAsync(It.IsAny<byte[]>())).ReturnsAsync(ocr);

            return new RecipeImportOrchestrationService(
                db,
                NullLogger<RecipeImportOrchestrationService>.Instance,
                new Mock<IRecipeScrapingService>().Object,
                ocrService.Object);
        }

        private static OcrRecipeData FullRecipe() => new()
        {
            Title = "Buttermilk Pancakes",
            Description = "From a cookbook page.",
            Ingredients = new List<string> { "2 cups all-purpose flour", "1 tsp salt", "3 tbsp unsalted butter" },
            Instructions = new List<string> { "Whisk the dry ingredients.", "Fold in the butter.", "Cook on a griddle." },
            PrepTime = "10 minutes",
            CookTime = "15 minutes",
            TotalTime = "25 minutes",
            Yield = "12 pancakes",
        };

        private static async Task SeedCatalogAsync(ApplicationDbContext db)
        {
            db.Ingredients.AddRange(
                new IngredientEntity { Id = 1, Name = "All-Purpose Flour", CurationStatusId = 9003 },
                new IngredientEntity { Id = 2, Name = "Flour", CurationStatusId = 9003 },
                new IngredientEntity { Id = 3, Name = "Salt", CurationStatusId = 9003 });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Persists_the_steps_the_ocr_parsed()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);

            var result = await NewService(db, FullRecipe()).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            var steps = await db.RecipeSteps.Where(s => s.RecipeId == result.Id)
                .OrderBy(s => s.StepNumber).ToListAsync();
            steps.Should().HaveCount(3);
            steps.Select(s => s.StepNumber).Should().Equal(1, 2, 3);
            steps[0].Description.Should().Be("Whisk the dry ingredients.");
            steps[2].Description.Should().Be("Cook on a griddle.");
        }

        [Fact]
        public async Task Persists_times_and_yield()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);

            var result = await NewService(db, FullRecipe()).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            var recipe = await db.Recipes.SingleAsync(r => r.Id == result.Id);
            recipe.PrepTime.Should().Be("10 minutes");
            recipe.CookTime.Should().Be("15 minutes");
            recipe.TotalTime.Should().Be("25 minutes");
            recipe.RecipeYield.Should().Be("12 pancakes");
        }

        [Fact]
        public async Task Links_ingredient_lines_to_the_catalog_keeping_the_raw_line()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);

            var result = await NewService(db, FullRecipe()).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            var rows = await db.RecipeIngredients.Where(ri => ri.RecipeId == result.Id).ToListAsync();
            rows.Should().HaveCount(2);
            // Longest catalog name wins, so the flour line links to All-Purpose Flour.
            rows.Should().Contain(r => r.IngredientId == 1 && r.RawLine == "2 cups all-purpose flour");
            rows.Should().Contain(r => r.IngredientId == 3 && r.RawLine == "1 tsp salt");
            // 0 is the "not parsed" convention; the raw line holds the truth.
            rows.Should().OnlyContain(r => r.Quantity == 0m);
        }

        [Fact]
        public async Task Never_invents_a_catalog_entry_from_a_raw_line()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);
            var before = await db.Ingredients.CountAsync();

            var result = await NewService(db, FullRecipe()).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            (await db.Ingredients.CountAsync()).Should().Be(before);
            result.Message.Should().Contain("unsalted butter");
        }

        [Fact]
        public async Task Reports_what_was_captured_instead_of_a_bare_success()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);

            var result = await NewService(db, FullRecipe()).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            result.Message.Should().Contain("3 steps");
            result.Message.Should().Contain("2 ingredients");
            result.Message.Should().Contain("1 ingredient line");
        }

        [Fact]
        public async Task Word_boundaries_stop_salt_matching_unsalted_butter()
        {
            using var db = NewContext();
            db.Ingredients.Add(new IngredientEntity { Id = 3, Name = "Salt", CurationStatusId = 9003 });
            await db.SaveChangesAsync();

            var ocr = FullRecipe();
            ocr.Ingredients = new List<string> { "3 tbsp unsalted butter" };
            var result = await NewService(db, ocr).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            (await db.RecipeIngredients.CountAsync(ri => ri.RecipeId == result.Id)).Should().Be(0);
        }

        [Fact]
        public async Task A_line_is_not_linked_through_an_alias_with_no_context()
        {
            using var db = NewContext();
            db.Ingredients.Add(new IngredientEntity { Id = 4, Name = "Light Cream", CurationStatusId = 9003 });
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = 4, AliasName = "cream" },
                new IngredientAliasEntity { IngredientId = 4, AliasName = "half-and-half", SourceContext = "link:ai:fake" });
            await db.SaveChangesAsync();

            var ocr = FullRecipe();
            ocr.Ingredients = new List<string> { "1 cup cream", "2 cups half-and-half" };
            var result = await NewService(db, ocr).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            (await db.RecipeIngredients.CountAsync(ri => ri.RecipeId == result.Id)).Should().Be(0);
            result.Message.Should().Contain("1 cup cream").And.Contain("2 cups half-and-half");
        }

        [Fact]
        public async Task A_line_is_linked_through_a_trusted_alias()
        {
            using var db = NewContext();
            db.Ingredients.Add(new IngredientEntity { Id = 5, Name = "Eggs", CurationStatusId = 9003 });
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = 5, AliasName = "large egg", SourceContext = "deterministic:recheck:staple" });
            await db.SaveChangesAsync();

            var ocr = FullRecipe();
            ocr.Ingredients = new List<string> { "1 large egg, beaten" };
            var result = await NewService(db, ocr).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            var row = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == result.Id);
            row.IngredientId.Should().Be(5);
            row.RawLine.Should().Be("1 large egg, beaten");
        }

        [Fact]
        public async Task Flags_transcribed_prose_so_it_stays_out_of_public_listings()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);

            var result = await NewService(db, FullRecipe()).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            var recipe = await db.Recipes.SingleAsync(r => r.Id == result.Id);
            recipe.ContainsSourceProse.Should().BeTrue();
            recipe.IsOcrRecipe.Should().BeTrue();
        }

        [Fact]
        public async Task A_food_named_on_two_lines_keeps_both_lines_in_page_order()
        {
            using var db = NewContext();
            await SeedCatalogAsync(db);

            var ocr = FullRecipe();
            ocr.Ingredients = new List<string> { "1 cup flour, for the batter", "1 tsp salt", "2 tbsp flour, for dusting" };
            var result = await NewService(db, ocr).ImportFromImageAsync(new byte[] { 1 }, AuthorId);

            var rows = await db.RecipeIngredients.Where(ri => ri.RecipeId == result.Id).OrderBy(ri => ri.SortOrder).ToListAsync();
            rows.Select(r => (r.SortOrder, r.IngredientId, r.RawLine)).Should().Equal(
                (0, 2L, "1 cup flour, for the batter"),
                (1, 3L, "1 tsp salt"),
                (2, 2L, "2 tbsp flour, for dusting"));
            result.Message.Should().Contain("3 ingredients");
        }
    }
}
