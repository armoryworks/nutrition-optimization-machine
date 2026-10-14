using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Import.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// The FDC importers store the FDA label nutrients (sodium, fibre, cholesterol, saturated and
    /// trans fat, vitamin D, calcium, iron, potassium) per 100 g in each nutrient's own unit, and
    /// --backfill-nutrients fills them onto FdcId-linked ingredients only where missing.
    /// </summary>
    public class FdcLabelNutrientImportTests : IDisposable
    {
        private const long Gram = 1, Milligram = 8, Microgram = 9, Kcal = 16;
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, SatFat = 5001, TransFat = 5037,
            Cholesterol = 5002, Sodium = 5004, Fiber = 5005, VitaminD = 5010, Calcium = 5022, Iron = 5023, Potassium = 5034;

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdc-label-" + Guid.NewGuid().ToString("N"));

        public FdcLabelNutrientImportTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n" +
                "\"171287\",\"sr_legacy_food\",\"Egg, whole, raw, fresh\",\"1\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_dir, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n" +
                "\"1\",\"171287\",\"1008\",\"143\"\n" +
                "\"2\",\"171287\",\"1003\",\"12.56\"\n" +
                "\"3\",\"171287\",\"1005\",\"0.72\"\n" +
                "\"4\",\"171287\",\"1004\",\"9.51\"\n" +
                "\"5\",\"171287\",\"1258\",\"3.126\"\n" +
                "\"6\",\"171287\",\"1257\",\"0.038\"\n" +
                "\"7\",\"171287\",\"1253\",\"372\"\n" +
                "\"8\",\"171287\",\"1093\",\"142\"\n" +
                "\"9\",\"171287\",\"2033\",\"0\"\n" +
                "\"10\",\"171287\",\"1110\",\"82\"\n" +
                "\"11\",\"171287\",\"1087\",\"56\"\n" +
                "\"12\",\"171287\",\"1089\",\"1.75\"\n" +
                "\"13\",\"171287\",\"1092\",\"138000\"\n");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private static async Task<ApplicationDbContext> NewContextAsync(bool withTransFat = true)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Milligram, Name = "Milligram", Symbol = "mg", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Microgram, Name = "Microgram", Symbol = "µg", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 4 });
            db.Set<NutrientEntity>().AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = Carbs, Name = "Total Carbohydrates", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = Fat, Name = "Fat", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = SatFat, Name = "Saturated Fat", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = Cholesterol, Name = "Cholesterol", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = Sodium, Name = "Sodium", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = Fiber, Name = "Dietary Fiber", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = VitaminD, Name = "Vitamin D", DefaultMeasurementId = Microgram },
                new NutrientEntity { Id = Calcium, Name = "Calcium", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = Iron, Name = "Iron", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = Potassium, Name = "Potassium", DefaultMeasurementId = Milligram });
            if (withTransFat)
                db.Set<NutrientEntity>().Add(new NutrientEntity { Id = TransFat, Name = "Trans Fat", DefaultMeasurementId = Gram });
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<decimal?> AmountAsync(ApplicationDbContext db, long ingredientId, long nutrientId) =>
            (await db.Set<IngredientNutrientEntity>().SingleOrDefaultAsync(n => n.IngredientId == ingredientId && n.NutrientId == nutrientId))?.Amount;

        [Fact]
        public async Task Import_stores_label_nutrients_in_each_nutrients_unit_and_drops_only_implausible_values()
        {
            using var db = await NewContextAsync();
            var report = await new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance)
                .ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);

            report.Accepted.Should().Be(1);
            var egg = await db.Ingredients.SingleAsync();
            (await AmountAsync(db, egg.Id, SatFat)).Should().Be(3.126m);
            (await AmountAsync(db, egg.Id, TransFat)).Should().Be(0.038m);
            (await AmountAsync(db, egg.Id, Cholesterol)).Should().Be(372m);
            (await AmountAsync(db, egg.Id, Sodium)).Should().Be(142m);
            (await AmountAsync(db, egg.Id, Fiber)).Should().Be(0m, "AOAC 2011.25 fibre (2033) stands in when 1079 is absent");
            (await AmountAsync(db, egg.Id, VitaminD)).Should().Be(2.05m, "82 IU ÷ 40 IU/µg");
            (await AmountAsync(db, egg.Id, Calcium)).Should().Be(56m);
            (await AmountAsync(db, egg.Id, Iron)).Should().BeNull("its Nutrient row is in grams, not the milligrams FDC reports");
            (await AmountAsync(db, egg.Id, Potassium)).Should().BeNull("138 000 mg/100 g is impossible, so the value is dropped, not zeroed");
            report.DroppedValuesByReason.Should().ContainKey("potassium_impossible");

            var sodiumRow = await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == egg.Id && n.NutrientId == Sodium);
            sodiumRow.MeasurementId.Should().Be(Milligram);
            (await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == egg.Id && n.NutrientId == VitaminD))
                .MeasurementId.Should().Be(Microgram);
        }

        [Fact]
        public async Task Backfill_fills_only_missing_nutrients_on_linked_ingredients_and_reruns_write_nothing()
        {
            using var db = await NewContextAsync();
            var egg = new IngredientEntity { Name = "Egg", FdcId = "171287", CurationStatusId = 9003 };
            var unlinked = new IngredientEntity { Name = "House spice mix", CurationStatusId = 9003 };
            db.Ingredients.AddRange(egg, unlinked);
            await db.SaveChangesAsync();
            db.Set<IngredientNutrientEntity>().AddRange(
                new IngredientNutrientEntity { IngredientId = egg.Id, NutrientId = Calories, Amount = 143m, MeasurementId = Kcal },
                new IngredientNutrientEntity { IngredientId = egg.Id, NutrientId = Sodium, Amount = 140m, MeasurementId = Milligram });
            await db.SaveChangesAsync();
            var backfill = new FdcNutrientBackfillService(db, NullLogger<FdcNutrientBackfillService>.Instance);

            var dry = await backfill.BackfillAsync(_dir, dryRun: true);

            dry.LinkedIngredients.Should().Be(1);
            dry.Rows.Should().BeGreaterThan(0);
            dry.RowsByNutrient.Should().ContainKey("cholesterol").And.NotContainKey("sodium").And.NotContainKey("calories");
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(2, "a dry run saves nothing");

            var real = await backfill.BackfillAsync(_dir, dryRun: false);

            real.Rows.Should().Be(dry.Rows);
            real.IngredientsFilled.Should().Be(1);
            (await AmountAsync(db, egg.Id, Sodium)).Should().Be(140m, "a stored value is never overwritten");
            (await AmountAsync(db, egg.Id, Cholesterol)).Should().Be(372m);
            (await AmountAsync(db, egg.Id, Protein)).Should().Be(12.56m);
            (await db.Set<IngredientNutrientEntity>().AnyAsync(n => n.IngredientId == unlinked.Id)).Should().BeFalse();

            (await backfill.BackfillAsync(_dir, dryRun: false)).Rows.Should().Be(0, "the backfill is idempotent");
        }

        [Fact]
        public async Task Backfill_names_the_nutrients_the_database_has_no_row_for()
        {
            using var db = await NewContextAsync(withTransFat: false);

            var report = await new FdcNutrientBackfillService(db, NullLogger<FdcNutrientBackfillService>.Instance)
                .BackfillAsync(_dir, dryRun: true);

            report.UnresolvedNutrients.Should().Contain("trans_fat").And.Contain("iron");
        }

        [Fact]
        public async Task Branded_import_stores_label_nutrients_too()
        {
            File.WriteAllText(Path.Combine(_dir, "branded_food.csv"),
                "\"fdc_id\",\"brand_owner\",\"gtin_upc\",\"serving_size\",\"serving_size_unit\",\"branded_food_category\",\"market_country\",\"discontinued_date\"\n" +
                "\"171287\",\"Happy Hens\",\"012345678905\",\"50\",\"g\",\"Eggs & Egg Substitutes\",\"United States\",\"\"\n");
            using var db = await NewContextAsync();

            var report = await new FdcBrandedImportService(db, NullLogger<FdcBrandedImportService>.Instance).ImportAsync(_dir, limit: 10);

            report.Accepted.Should().Be(1);
            var egg = await db.Ingredients.SingleAsync();
            (await AmountAsync(db, egg.Id, Sodium)).Should().Be(142m);
            (await AmountAsync(db, egg.Id, SatFat)).Should().Be(3.126m);
            (await AmountAsync(db, egg.Id, VitaminD)).Should().Be(2.05m);
        }
    }
}
