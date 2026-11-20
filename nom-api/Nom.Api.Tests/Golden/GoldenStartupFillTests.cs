using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Import.Golden;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The reviewed lines of startup-fills.tsv against the start-up repair: each line stating no
    /// amount is put in a recipe with its method and the other lines, under an open line journal
    /// when the row names one, and the amount the repair gives it is compared.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenStartupFillTests
    {
        private static readonly string[] Units = { "Gram", "Piece", "Pound", "Ounce", "Cup", "Tablespoon", "Teaspoon", "Pinch", "Quart", "Gallon" };

        private readonly ITestOutputHelper _output;

        public GoldenStartupFillTests(ITestOutputHelper output) => _output = output;

        private static string Fill(GoldenRow row)
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var units = Units.Select((name, i) => new BaseMeasurementEntity { Id = i + 1, Name = name, Symbol = name.ToLowerInvariant(), MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m }).ToList();
            db.Set<BaseMeasurementEntity>().AddRange(units);
            var lines = new[] { row["part"] }.Concat(row["others"] == GoldenTsv.NoAnswer ? Array.Empty<string>() : row["others"].Split(" ¦ ")).ToList();
            var foods = lines.Select(l => new IngredientEntity { Name = IngredientNameNormalizer.Normalize(l) is { Length: > 0 } n ? n : l, CurationStatusId = 9003 }).ToList();
            db.Ingredients.AddRange(foods);
            db.SaveChanges();

            var recipe = new RecipeEntity
            {
                Name = "Golden",
                ScrapedAtUtc = DateTime.UtcNow,
                CurationStatusId = 9000,
                RecipeSteps = row["steps"].Split(" ¦ ").Select((s, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = "Step", Description = s }).ToList(),
                RecipeIngredients = lines.Select((l, i) =>
                {
                    var parsed = i == 0 ? null : IngredientLineParser.Parse(l);
                    return new RecipeIngredientEntity
                    {
                        IngredientId = foods[i].Id,
                        RawLine = l,
                        Quantity = parsed?.Quantity ?? 0m,
                        MeasurementId = parsed != null && units.FirstOrDefault(u => u.Name == parsed.MeasurementName) is { } unit ? unit.Id : 1,
                    };
                }).ToList(),
            };
            db.Recipes.Add(recipe);
            db.SaveChanges();
            if (row["journal"] != GoldenTsv.NoAnswer)
            {
                db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = ScrapedRecipeRepairService.LineEntityType,
                    EntityId = recipe.Id,
                    ChangeType = row["journal"],
                    PropertyName = nameof(RecipeIngredientEntity.RawLine),
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = SystemConstants.SystemPersonId,
                });
                db.SaveChanges();
            }
            db.ChangeTracker.Clear();

            new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 10).GetAwaiter().GetResult();
            var filled = db.RecipeIngredients.AsNoTracking().Single(ri => ri.RecipeId == recipe.Id && ri.IngredientId == foods[0].Id);
            return filled.Quantity == 0 ? GoldenTsv.NoAnswer : $"{GoldenSet.Number(filled.Quantity)} {units.Single(u => u.Id == filled.MeasurementId).Name}";
        }

        [Fact]
        public void Each_reviewed_line_gets_the_start_up_amount_as_reviewed()
        {
            var report = GoldenSet.Run("startup-fills.tsv", "status", row => GoldenSet.Compare(row, new Dictionary<string, string> { ["expect"] = Fill(row) }));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
