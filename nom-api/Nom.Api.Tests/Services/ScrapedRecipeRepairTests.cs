using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Harvested recipes reached prod with Quantity 0 on every ingredient row, which failed vetting
    /// and parked them in RequiresRevision. The repair rebuilds quantities from RawLine and re-vets
    /// only recipes no human has reviewed.
    /// </summary>
    public class ScrapedRecipeRepairTests
    {
        private static ApplicationDbContext NewContext()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = 3, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 },
                new BaseMeasurementEntity { Id = 11, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = 12, Name = "Tablespoon", Symbol = "tbsp", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = 13, Name = "Teaspoon", Symbol = "tsp", MeasurementCategoryId = 2 });
            db.SaveChanges();
            return db;
        }

        private static RecipeEntity Harvested(string name, long status, params string[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = name,
                ScrapedAtUtc = DateTime.UtcNow,
                CurationStatusId = status,
                VettingIssues = $"{lines.Length} of {lines.Length} ingredient lines have no parseable quantity — needs a human (or enrichment) pass.",
                RecipeSteps = new List<RecipeStepEntity>
                {
                    new() { StepNumber = 1, Summary = "Mix", Description = "Whisk everything together in a bowl." },
                    new() { StepNumber = 2, Summary = "Bake", Description = "Bake until golden, about 12 minutes." },
                },
            };
            var ingredientId = 100L * (status + name.Length);
            recipe.RecipeIngredients = lines.Select(l => new RecipeIngredientEntity
            {
                IngredientId = ingredientId++,
                Quantity = 0,
                MeasurementId = 1,
                RawLine = l,
            }).ToList();
            return recipe;
        }

        [Fact]
        public async Task Rebuilds_quantities_and_clears_quantity_only_vetting_flags()
        {
            using var db = NewContext();
            db.Recipes.Add(Harvested("Cookies", (long)CurationStatusEnum.RequiresRevision,
                "2 large eggs", "1 tablespoon vanilla extract", "¾ teaspoon salt", "1 ⅔ cups bread flour", "flaky sea salt (for topping)"));
            await db.SaveChangesAsync();

            var result = await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 50);

            result.RowsRepaired.Should().Be(4);
            result.RowsStillUnparsed.Should().Be(1);
            result.Cleared.Should().Be(1);
            var recipe = await db.Recipes.Include(r => r.RecipeIngredients).SingleAsync();
            recipe.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated, "clean vetting leaves it uncurated, never approved");
            recipe.VettingIssues.Should().BeNull();
            var eggs = recipe.RecipeIngredients!.Single(i => i.RawLine == "2 large eggs");
            eggs.Quantity.Should().Be(2);
            eggs.MeasurementId.Should().Be(3, "a count of eggs is pieces, not grams");
            recipe.RecipeIngredients!.Single(i => i.RawLine!.StartsWith("1 ⅔")).MeasurementId.Should().Be(11);
        }

        [Fact]
        public async Task Other_vetting_problems_keep_the_recipe_flagged()
        {
            using var db = NewContext();
            var recipe = Harvested("Stew", (long)CurationStatusEnum.RequiresRevision, "2 cups stock", "1 teaspoon salt");
            recipe.RecipeSteps!.Remove(recipe.RecipeSteps.Last());
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 50);

            var saved = await db.Recipes.SingleAsync();
            saved.CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
            saved.VettingIssues.Should().Contain("instruction step").And.NotContain("parseable quantity");
        }

        [Fact]
        public async Task Approved_and_reviewed_recipes_keep_their_status()
        {
            using var db = NewContext();
            var approved = Harvested("Approved", (long)CurationStatusEnum.Curated, "2 eggs", "1 cup milk");
            var reviewed = Harvested("Reviewed", (long)CurationStatusEnum.RequiresRevision, "2 eggs", "1 cup milk");
            reviewed.DateCurationCompleted = DateTime.UtcNow;
            db.Recipes.AddRange(approved, reviewed);
            await db.SaveChangesAsync();

            var result = await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 50);

            result.Revetted.Should().Be(0);
            (await db.Recipes.SingleAsync(r => r.Name == "Approved")).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            (await db.Recipes.SingleAsync(r => r.Name == "Reviewed")).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
            result.RowsRepaired.Should().Be(4, "quantities are still rebuilt — they're data, not a curation decision");
        }

        [Fact]
        public async Task Cursor_advances_and_reports_done()
        {
            using var db = NewContext();
            db.Recipes.Add(Harvested("One", (long)CurationStatusEnum.RequiresRevision, "1 cup flour", "2 eggs"));
            db.Recipes.Add(Harvested("Two", (long)CurationStatusEnum.RequiresRevision, "1 cup sugar", "2 eggs"));
            await db.SaveChangesAsync();
            var service = new ScrapedRecipeRepairService(db, new RecipeVettingService());

            var first = await service.RepairBatchAsync(0, 1);
            var second = await service.RepairBatchAsync(first.LastRecipeId, 1);
            var done = await service.RepairBatchAsync(second.LastRecipeId, 1);

            first.Recipes.Should().Be(1);
            second.Recipes.Should().Be(1);
            done.Recipes.Should().Be(0);
        }

        [Fact]
        public async Task Import_path_parses_lines_the_scraper_left_unparsed()
        {
            var parsed = Nom.Orch.Services.Support.IngredientLineParser.Normalize(null, null, "2 large eggs");
            parsed!.Quantity.Should().Be(2);
            Nom.Orch.Services.Support.IngredientLineParser.Normalize(1.5m, "c", "1 1/2 c milk")!.MeasurementName.Should().Be("Cup");
            Nom.Orch.Services.Support.IngredientLineParser.Normalize(1m, "quart", "1 quart stock")!.Quantity.Should().Be(4);
            Nom.Orch.Services.Support.IngredientLineParser.Normalize(3m, null, "3 apples")!.MeasurementName.Should().Be("Piece");
            await Task.CompletedTask;
        }
    }
}
