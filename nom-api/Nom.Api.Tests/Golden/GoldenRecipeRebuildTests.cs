using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Import.Golden;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The recipes of recipes-rebuild.tsv rebuilt in place from their stored source: each row puts a
    /// recipe with its stored rows, its curated foods and its source record (version 2 or more for a
    /// newer extraction of the book) in a database of its own, applies the rebuild and compares the
    /// rows after, in order, with the row's expectation.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenRecipeRebuildTests
    {
        public const string File = "recipes-rebuild.tsv";

        private static readonly string[] Units =
        {
            "Piece", "Cup", "Teaspoon", "Tablespoon", "Gram", "Kilogram", "Ounce", "Pound", "Pinch", "Clove", "Stalk", "Slice",
            "Package", "Dozen", "Can", "Liter", "Milliliter",
        };

        private static readonly string[] Method = { "Mix everything well.", "Bake until done." };

        private readonly ITestOutputHelper _output;

        public GoldenRecipeRebuildTests(ITestOutputHelper output) => _output = output;

        private sealed class SystemUser : ICurrentUserService
        {
            public long? PersonId => SystemConstants.SystemPersonId;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => SystemConstants.SystemPersonId;
            public string? UserId => "system";
            public string RequiredUserId => "system";
        }

        private static IReadOnlyList<string> Cells(string value) => value == GoldenTsv.NoAnswer ? Array.Empty<string>() : value.Split(" ¦ ");

        private static (string Food, decimal Quantity, string? Unit, string? Kind) Row(string text)
        {
            var kindAt = text.LastIndexOf('#');
            var kind = kindAt >= 0 ? text[(kindAt + 1)..] : null;
            var body = kindAt >= 0 ? text[..kindAt] : text;
            var colon = body.LastIndexOf(':');
            var food = body[..colon];
            var amount = body[(colon + 1)..];
            if (amount == GoldenTsv.NoAnswer) return (food, 0m, null, kind);
            var parts = amount.Split(' ', 2);
            return (food, decimal.Parse(parts[0], CultureInfo.InvariantCulture), parts[1], kind);
        }

        public static string Rebuilt(GoldenRow row) => RebuiltAsync(row).GetAwaiter().GetResult();

        private static async Task<string> RebuiltAsync(GoldenRow row)
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var units = Units.Select((name, i) => new BaseMeasurementEntity
            {
                Id = i + 1, Name = name, Symbol = name.ToLowerInvariant(), MeasurementCategoryId = 1, IsContainer = name is "Can" or "Package",
            }).ToList();
            db.Measurements.AddRange(units);

            var foods = new Dictionary<string, IngredientEntity>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Cells(row["catalog"]))
            {
                var parts = entry.Split('|');
                foods[parts[0]] = new IngredientEntity
                {
                    Name = parts[0], CurationStatusId = (long)CurationStatusEnum.Curated,
                    GramsPerItem = parts.Length > 1 ? decimal.Parse(parts[1], CultureInfo.InvariantCulture) : null,
                };
            }
            var stored = Cells(row["stored"]).Select(entry =>
            {
                var at = entry.LastIndexOf('@');
                var rest = entry[(at + 1)..];
                var equals = rest.IndexOf('=');
                var (food, quantity, unit, kind) = Row(rest[(equals + 1)..]);
                if (!foods.ContainsKey(food)) foods[food] = new IngredientEntity { Name = food, CurationStatusId = (long)CurationStatusEnum.NonCurated };
                return (Raw: entry[..at], Position: int.Parse(rest[..equals], CultureInfo.InvariantCulture), Food: food, Quantity: quantity, Unit: unit, Kind: kind);
            }).ToList();
            db.Ingredients.AddRange(foods.Values);
            await db.SaveChangesAsync();

            var steps = Cells(row["steps"]) is { Count: > 0 } given ? given : Method;
            var recipe = new RecipeEntity
            {
                Name = "Golden " + row.Id,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                ScrapedAtUtc = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
                LicenseStatus = RecipeLicenseStatus.PublicDomain,
                SourceAttribution = "Golden book",
                RecipeIngredients = stored.Select(s => new RecipeIngredientEntity
                {
                    IngredientId = foods[s.Food].Id, SortOrder = s.Position, Quantity = s.Quantity, RawLine = s.Raw, LineKind = s.Kind,
                    MeasurementId = s.Unit == null ? 1 : units.Single(u => u.Name == s.Unit).Id,
                }).ToList(),
                RecipeSteps = steps.Select((s, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = s, Description = s }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            var record = new ScraperRecipe
            {
                Name = recipe.Name,
                PublicDomain = true,
                Ingredients = Cells(row["lines"]).Select(l => new ScraperIngredient { RawLine = l }).ToList(),
                Steps = steps.Select((s, i) => new ScraperStep { Order = i + 1, Instruction = s }).ToList(),
            };
            var version = int.Parse(row["version"], CultureInfo.InvariantCulture);
            for (var v = 1; v <= version; v++)
            {
                var content = SourceRecord.Content(record);
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id, RawJsonLd = content, Format = ScrapedDocumentFormat.StagedCookbook, ContentSha256 = SourceRecord.Sha256(content),
                    Version = v, FetchedAtUtc = recipe.ScrapedAtUtc!.Value,
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var service = new RecipeReprocessService(db, new SystemUser(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(),
                new Mock<IRecipeNutritionService>().Object, NullLogger<RecipeReprocessService>.Instance);
            var result = await service.RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = new List<long> { recipe.Id } }, apply: true);
            if (result.Results.Single() is { Outcome: RecipeReprocessOutcome.Held or RecipeReprocessOutcome.Failed } held)
                return $"{held.Outcome}: {held.Reason}";

            var names = await db.Ingredients.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name);
            var lines = await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipe.Id).OrderBy(ri => ri.SortOrder).ToListAsync();
            return string.Join(" ¦ ", lines.Select(l =>
                $"{names[l.IngredientId]}:{(l.Quantity == 0 ? GoldenTsv.NoAnswer : $"{GoldenSet.Number(l.Quantity)} {units.Single(u => u.Id == l.MeasurementId).Name}")}"
                + (l.LineKind is { } kind && kind != RecipeIngredientLineKind.Ingredient ? "#" + kind : string.Empty)));
        }

        [Fact]
        public void Each_reviewed_recipe_rebuilds_to_the_reviewed_lines()
        {
            var report = GoldenSet.Run(File, "status", row => GoldenSet.Compare(row, new Dictionary<string, string> { ["expect"] = Rebuilt(row) }),
                modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
