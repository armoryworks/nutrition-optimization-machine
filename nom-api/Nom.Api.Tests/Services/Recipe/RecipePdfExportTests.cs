using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityServices;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The recipe PDF export is drawn by MigraDoc with Lato embedded from Nom.Orch, so it renders the same
    /// on a host with no installed fonts. Text is read back with an independent parser (PdfPig).
    /// </summary>
    public class RecipePdfExportTests
    {
        private static RecipePdfRecipe Pancakes() => new(
            "Buttermilk Pancakes",
            "Fluffy weekend pancakes.",
            new[] { "Prep: 10 min", "Cook: 15 min", "Total: 25 min" },
            new[] { "2 cups flour", "1 3/4 cups buttermilk" },
            new[] { "Whisk the dry ingredients.", "Fold in the buttermilk and cook on a hot griddle." });

        private static string[] PageTexts(byte[] pdf)
        {
            using var document = PdfDocument.Open(pdf);
            return document.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)).ToArray();
        }

        [Fact]
        public void Renders_a_letter_pdf_with_every_section_in_order()
        {
            var pdf = RecipePdfRenderer.Render(new RecipePdfExport(new DateTime(2026, 10, 9), 2, new[]
            {
                Pancakes(),
                new RecipePdfRecipe("Plain Toast", null, Array.Empty<string>(), null, new[] { "Toast the bread." }),
            }));

            Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
            pdf.Length.Should().BeGreaterThan(5_000);

            using var document = PdfDocument.Open(pdf);
            document.NumberOfPages.Should().Be(1);
            var page = document.GetPage(1);
            page.Width.Should().BeApproximately(612, 0.5);
            page.Height.Should().BeApproximately(792, 0.5);
            page.Letters.Select(l => l.FontName).Distinct().Should().OnlyContain(f => f.Contains("Lato"));

            var text = ContentOrderTextExtractor.GetText(page);
            var expected = new[]
            {
                "Recipe Export", "Exported: 2026-10-09", "2 recipe(s)",
                "Buttermilk Pancakes", "Fluffy weekend pancakes.", "Prep: 10 min | Cook: 15 min | Total: 25 min",
                "Ingredients", "• 2 cups flour", "• 1 3/4 cups buttermilk",
                "Instructions", "1. Whisk the dry ingredients.", "2. Fold in the buttermilk and cook on a hot griddle.",
                "Plain Toast", "Instructions", "1. Toast the bread.",
            };
            var position = 0;
            foreach (var fragment in expected)
            {
                var found = text.IndexOf(fragment, position, StringComparison.Ordinal);
                found.Should().BeGreaterThanOrEqualTo(0, $"\"{fragment}\" should follow the previous section in:\n{text}");
                position = found + fragment.Length;
            }
            text.Should().Contain("Page 1 of 1");
        }

        [Fact]
        public void Long_exports_repeat_the_header_and_number_every_page()
        {
            var recipes = Enumerable.Range(1, 12).Select(i => Pancakes() with { Name = $"Pancakes {i}" }).ToArray();

            var pages = PageTexts(RecipePdfRenderer.Render(new RecipePdfExport(DateTime.UtcNow, recipes.Length, recipes)));

            pages.Length.Should().BeGreaterThan(1);
            for (var i = 0; i < pages.Length; i++)
            {
                pages[i].Should().StartWith("Recipe Export");
                pages[i].Should().Contain($"Page {i + 1} of {pages.Length}");
            }
            string.Join("\n", pages).Should().Contain("Pancakes 12");
        }

        [Fact]
        public void Characters_outside_win_ansi_survive_the_round_trip()
        {
            var recipe = new RecipePdfRecipe("Crème brûlée", null, Array.Empty<string>(), new[] { "⅓ cup sugar", "bake at 150 °C – 40 min" }, null);

            var text = PageTexts(RecipePdfRenderer.Render(new RecipePdfExport(DateTime.UtcNow, 1, new[] { recipe }))).Single();

            text.Should().Contain("Crème brûlée").And.Contain("• ⅓ cup sugar").And.Contain("• bake at 150 °C – 40 min");
        }

        [Fact]
        public async Task Export_service_writes_the_pdf_from_stored_recipes()
        {
            var dbName = Guid.NewGuid().ToString();
            DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options;
            using (var seed = new ApplicationDbContext(Options()))
            {
                seed.Persons.Add(new PersonEntity { Id = 7, Name = "Cook", UserId = "u-cook" });
                seed.References.Add(new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" });
                seed.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity { Id = 11, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 });
                seed.Recipes.Add(new RecipeEntity
                {
                    Id = 1,
                    Name = "Rice Pilaf",
                    AuthorId = 7,
                    Visibility = RecipeVisibilityEnum.Private,
                    CurationStatusId = (long)CurationStatusEnum.NonCurated,
                    PrepTime = "5 min",
                    RecipeIngredients = new List<RecipeIngredientEntity>
                    {
                        new() { IngredientId = 100, Ingredient = new IngredientEntity { Id = 100, Name = "rice" }, Quantity = 1, MeasurementId = 11, RawLine = "1 cup long-grain rice" },
                        new() { IngredientId = 101, Ingredient = new IngredientEntity { Id = 101, Name = "stock" }, Quantity = 2, MeasurementId = 11 },
                    },
                    RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Simmer covered." } },
                });
                await seed.SaveChangesAsync();
            }

            using var db = new ApplicationDbContext(Options());
            var service = new RecipeBulkOperationsService(db, Mock.Of<IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(),
                NullLogger<RecipeBulkOperationsService>.Instance);

            var result = await service.ExportRecipesAsync(new RecipeBulkExportModel
            {
                RecipeIds = new List<long> { 1 }, RequesterPersonId = 7, ExportType = ExportTypes.Pdf,
            });

            result.Success.Should().BeTrue(string.Join(";", result.Errors));
            var path = Path.Combine(Directory.GetCurrentDirectory(), "exports", $"recipes_export_{result.ExportId}.pdf");
            try
            {
                var text = string.Join("\n", PageTexts(await File.ReadAllBytesAsync(path)));
                text.Should().Contain("1 recipe(s)").And.Contain("Rice Pilaf").And.Contain("Prep: 5 min")
                    .And.Contain("• 1 cup long-grain rice").And.Contain("• 2 Cup stock").And.Contain("1. Simmer covered.");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
