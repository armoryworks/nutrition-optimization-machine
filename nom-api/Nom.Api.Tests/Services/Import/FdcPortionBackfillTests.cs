using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Import.Services;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// The portion backfill fills only empty gram conversions from USDA portions, logs each value
    /// with its fdc: source, leaves admin-entered values alone, writes nothing on a dry run and can
    /// be undone.
    /// </summary>
    public class FdcPortionBackfillTests : IDisposable
    {
        private const long Gram = 1, Piece = 3, Cup = 11, Kcal = 16, Calories = 5035;
        private const string FoundationGarlic = "1104647", SrGarlic = "169230", SrHoney = "169640", SrStrawberries = "167762";

        private readonly string _foundation = Path.Combine(Path.GetTempPath(), "fdc-foundation-" + Guid.NewGuid().ToString("N"));
        private readonly string _sr = Path.Combine(Path.GetTempPath(), "fdc-sr-" + Guid.NewGuid().ToString("N"));

        public FdcPortionBackfillTests()
        {
            const string foodHeader = "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n";
            const string portionHeader = "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"portion_description\",\"modifier\",\"gram_weight\",\"data_points\",\"footnote\",\"min_year_acquired\"\n";
            const string units = "\"id\",\"name\"\n\"1000\",\"cup\"\n\"1001\",\"tablespoon\"\n\"9999\",\"undetermined\"\n";

            Directory.CreateDirectory(_foundation);
            File.WriteAllText(Path.Combine(_foundation, "food.csv"), foodHeader +
                $"\"{FoundationGarlic}\",\"foundation_food\",\"Garlic, raw\",\"11\",\"2020-10-30\"\n");
            File.WriteAllText(Path.Combine(_foundation, "food_portion.csv"), portionHeader);
            File.WriteAllText(Path.Combine(_foundation, "measure_unit.csv"), units);

            Directory.CreateDirectory(_sr);
            File.WriteAllText(Path.Combine(_sr, "food.csv"), foodHeader +
                $"\"{SrGarlic}\",\"sr_legacy_food\",\"Garlic, raw\",\"11\",\"2019-04-01\"\n" +
                $"\"{SrHoney}\",\"sr_legacy_food\",\"Honey\",\"19\",\"2019-04-01\"\n" +
                $"\"{SrStrawberries}\",\"sr_legacy_food\",\"Strawberries, raw\",\"9\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_sr, "food_portion.csv"), portionHeader +
                $"\"84478\",\"{SrGarlic}\",\"1\",\"1\",\"9999\",\"\",\"cup\",\"136\",\"\",\"\",\"\"\n" +
                $"\"84479\",\"{SrGarlic}\",\"2\",\"1\",\"9999\",\"\",\"tsp\",\"2.8\",\"\",\"\",\"\"\n" +
                $"\"84480\",\"{SrGarlic}\",\"3\",\"1\",\"9999\",\"\",\"clove\",\"3\",\"\",\"\",\"\"\n" +
                $"\"85264\",\"{SrHoney}\",\"1\",\"1\",\"9999\",\"\",\"cup\",\"339\",\"\",\"\",\"\"\n" +
                $"\"85266\",\"{SrHoney}\",\"3\",\"1\",\"9999\",\"\",\"packet (0.5 oz)\",\"14\",\"\",\"\",\"\"\n" +
                $"\"81922\",\"{SrStrawberries}\",\"4\",\"1\",\"9999\",\"\",\"cup, whole\",\"144\",\"\",\"\",\"\"\n" +
                $"\"81925\",\"{SrStrawberries}\",\"7\",\"1\",\"9999\",\"\",\"medium (1-1/4\"\" dia)\",\"12\",\"\",\"\",\"\"\n");
            File.WriteAllText(Path.Combine(_sr, "measure_unit.csv"), units);
        }

        public void Dispose()
        {
            Directory.Delete(_foundation, recursive: true);
            Directory.Delete(_sr, recursive: true);
        }

        private sealed record Seeded(ApplicationDbContext Db, long Garlic, long Honey, long Strawberries);

        private static async Task<Seeded> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 236.588m },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.Add(new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal });

            var garlic = new IngredientEntity { Name = "Garlic", FdcId = FoundationGarlic, CurationStatusId = 9003 };
            var honey = new IngredientEntity { Name = "Honey", FdcId = SrHoney, CurationStatusId = 9003, GramsPerMilliliter = 1.42m };
            var strawberries = new IngredientEntity { Name = "Strawberries", CurationStatusId = 9003 };
            db.Ingredients.AddRange(garlic, honey, strawberries);
            await db.SaveChangesAsync();

            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = strawberries.Id, FdcId = SrStrawberries,
                Field = FoodCatalogReviewService.FdcLinkField, ProposedValue = "999", Source = $"fdc:{SrStrawberries}",
                Status = FoodProposalStatus.Applied,
            });
            foreach (var i in new[] { garlic, honey, strawberries })
                db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = i.Id, NutrientId = Calories, Amount = 100m, MeasurementId = Kcal });

            var recipe = new RecipeEntity { Name = "Strawberry salad", AuthorId = 1, Servings = 2, CurationStatusId = 9003 };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.AddRange(
                new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = garlic.Id, Quantity = 2m, MeasurementId = Piece, RawLine = "", SortOrder = 0 },
                new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = strawberries.Id, Quantity = 1m, MeasurementId = Cup, RawLine = "", SortOrder = 1 },
                new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = honey.Id, Quantity = 1m, MeasurementId = Piece, RawLine = "", SortOrder = 2 });
            await db.SaveChangesAsync();
            return new Seeded(db, garlic.Id, honey.Id, strawberries.Id);
        }

        private FdcPortionBackfillService Service(ApplicationDbContext db) => new(db, NullLogger<FdcPortionBackfillService>.Instance);

        [Fact]
        public async Task Dry_run_reports_what_would_change_and_writes_nothing()
        {
            var s = await SeedAsync();
            using var db = s.Db;

            var report = await Service(db).BackfillAsync(new[] { _foundation, _sr }, dryRun: true);

            report.Changes.Should().HaveCount(4);
            report.NoConversionLinesBefore.Should().Be(3);
            report.NoConversionLinesAfter.Should().Be(1, "honey has no countable USDA portion");
            report.CuratedNoConversionRecipesAfter.Should().Be(1);
            db.ChangeTracker.Clear();
            (await db.Ingredients.CountAsync(i => i.GramsPerMilliliter != null || i.ReferenceServingGrams != null)).Should().Be(1);
            (await db.FoodCatalogProposals.CountAsync(p => p.Batch == FdcPortionBackfillService.Batch)).Should().Be(0);
        }

        [Fact]
        public async Task Fills_only_empty_fields_logs_each_value_and_a_rerun_changes_nothing()
        {
            var s = await SeedAsync();
            using var db = s.Db;

            var report = await Service(db).BackfillAsync(new[] { _foundation, _sr }, dryRun: false);

            db.ChangeTracker.Clear();
            var garlic = await db.Ingredients.SingleAsync(i => i.Id == s.Garlic);
            garlic.ReferenceServingGrams.Should().Be(3m, "a piece of garlic is a clove, borrowed from the SR Legacy food of the same description");
            garlic.GramsPerMilliliter.Should().Be(0.5748m);
            var strawberries = await db.Ingredients.SingleAsync(i => i.Id == s.Strawberries);
            strawberries.GramsPerMilliliter.Should().Be(0.6087m, "a linked catalog row is reached through its applied fdc_link");
            strawberries.ReferenceServingGrams.Should().Be(12m);
            (await db.Ingredients.SingleAsync(i => i.Id == s.Honey)).GramsPerMilliliter.Should().Be(1.42m, "an existing value is never overwritten");
            report.KeptExisting.Should().ContainSingle(c => c.IngredientId == s.Honey && c.Value == 1.4329m);

            var log = await db.FoodCatalogProposals.Where(p => p.Batch == FdcPortionBackfillService.Batch).ToListAsync();
            log.Should().HaveCount(4).And.OnlyContain(p => p.Status == FoodProposalStatus.Applied && p.Source.StartsWith("fdc:"));
            log.Single(p => p.IngredientId == s.Garlic && p.Field == FdcPortionBackfillService.CountField).Source.Should().Be($"fdc:{SrGarlic}");
            (await db.RecipeIngredients.Where(ri => ri.IngredientId == s.Garlic).Select(ri => ri.LastModifiedDate).SingleAsync())
                .Should().NotBeNull("touching the line queues the recipe for nutrition recalculation");

            var rerun = await Service(db).BackfillAsync(new[] { _foundation, _sr }, dryRun: false);
            rerun.Changes.Should().BeEmpty();
            rerun.NoConversionLinesBefore.Should().Be(1);
        }

        [Fact]
        public async Task Undo_clears_what_the_backfill_wrote_but_not_a_value_edited_since()
        {
            var s = await SeedAsync();
            using var db = s.Db;
            await Service(db).BackfillAsync(new[] { _foundation, _sr }, dryRun: false);
            db.ChangeTracker.Clear();
            var edited = await db.Ingredients.SingleAsync(i => i.Id == s.Strawberries);
            edited.ReferenceServingGrams = 14m;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undone = await Service(db).UndoAsync();

            undone.Should().Be(3);
            db.ChangeTracker.Clear();
            var garlic = await db.Ingredients.SingleAsync(i => i.Id == s.Garlic);
            garlic.ReferenceServingGrams.Should().BeNull();
            garlic.GramsPerMilliliter.Should().BeNull();
            var strawberries = await db.Ingredients.SingleAsync(i => i.Id == s.Strawberries);
            strawberries.ReferenceServingGrams.Should().Be(14m);
            strawberries.GramsPerMilliliter.Should().BeNull();
            (await db.FoodCatalogProposals.CountAsync(p => p.Batch == FdcPortionBackfillService.Batch && p.Status == FoodProposalStatus.Rejected)).Should().Be(3);
        }
    }
}
