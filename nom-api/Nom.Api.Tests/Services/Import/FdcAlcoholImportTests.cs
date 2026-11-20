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
using Nom.Import.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// SR Legacy wine and spirits import once the gate counts their alcohol (FDC nutrient 1018),
    /// and the alcohol is stored when the catalog has an Alcohol nutrient.
    /// </summary>
    public class FdcAlcoholImportTests : IDisposable
    {
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, Sugars = 5036, Alcohol = 5038;
        private const string Wine = "174837", Spirit = "174815", Mislabelled = "999001";
        private const string WineDescription = "Alcoholic beverage, wine, table, white";

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdc-alcohol-test-" + Guid.NewGuid().ToString("N"));

        public FdcAlcoholImportTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n" +
                $"\"{Wine}\",\"sr_legacy_food\",\"{WineDescription}\",\"14\",\"2019-04-01\"\n" +
                $"\"{Spirit}\",\"sr_legacy_food\",\"Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof\",\"14\",\"2019-04-01\"\n" +
                $"\"{Mislabelled}\",\"sr_legacy_food\",\"Alcoholic beverage, mislabelled\",\"14\",\"2019-04-01\"\n" +
                "\"170000\",\"foundation_food\",\"Not this one\",\"14\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_dir, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n" +
                $"\"1\",\"{Wine}\",\"1008\",\"82\"\n" +
                $"\"2\",\"{Wine}\",\"1003\",\"0.07\"\n" +
                $"\"3\",\"{Wine}\",\"1005\",\"2.6\"\n" +
                $"\"4\",\"{Wine}\",\"1004\",\"0\"\n" +
                $"\"5\",\"{Wine}\",\"1018\",\"10.3\"\n" +
                $"\"6\",\"{Wine}\",\"2000\",\"0.96\"\n" +
                $"\"7\",\"{Spirit}\",\"1008\",\"231\"\n" +
                $"\"8\",\"{Spirit}\",\"1003\",\"0\"\n" +
                $"\"9\",\"{Spirit}\",\"1005\",\"0\"\n" +
                $"\"10\",\"{Spirit}\",\"1004\",\"0\"\n" +
                $"\"11\",\"{Spirit}\",\"1018\",\"33.4\"\n" +
                $"\"12\",\"{Mislabelled}\",\"1008\",\"400\"\n" +
                $"\"13\",\"{Mislabelled}\",\"1003\",\"0\"\n" +
                $"\"14\",\"{Mislabelled}\",\"1005\",\"2.6\"\n" +
                $"\"15\",\"{Mislabelled}\",\"1004\",\"0\"\n" +
                $"\"16\",\"{Mislabelled}\",\"1018\",\"10\"\n");
            File.WriteAllText(Path.Combine(_dir, "food_portion.csv"),
                "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"portion_description\",\"modifier\",\"gram_weight\",\"data_points\",\"footnote\",\"min_year_acquired\"\n" +
                $"\"94976\",\"{Wine}\",\"1\",\"1\",\"9999\",\"\",\"fl oz\",\"29.4\",\"\",\"\",\"\"\n" +
                $"\"94977\",\"{Wine}\",\"2\",\"1\",\"9999\",\"\",\"serving (5 fl oz)\",\"147\",\"\",\"\",\"\"\n");
            File.WriteAllText(Path.Combine(_dir, "measure_unit.csv"), "\"id\",\"name\"\n\"9999\",\"undetermined\"\n");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private static async Task<ApplicationDbContext> NewContextAsync(bool withAlcoholNutrient = true)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = 16, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 4 });
            db.Set<NutrientEntity>().AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = 16 },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Carbs, Name = "Total Carbohydrates", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Fat, Name = "Fat", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Sugars, Name = "Total Sugars", DefaultMeasurementId = 1, ParentNutrientId = Carbs });
            if (withAlcoholNutrient)
                db.Set<NutrientEntity>().Add(new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = 1 });
            await db.SaveChangesAsync();
            return db;
        }

        private static FdcFoundationImportService Importer(ApplicationDbContext db) =>
            new(db, NullLogger<FdcFoundationImportService>.Instance);

        [Fact]
        public async Task Wine_and_spirits_import_with_their_alcohol_and_a_food_alcohol_does_not_explain_is_rejected()
        {
            using var db = await NewContextAsync();

            var report = await Importer(db).ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);

            report.TotalFoundation.Should().Be(3);
            report.Accepted.Should().Be(2);
            report.Rejected.Should().Be(1);
            report.RejectedByReason.Should().ContainKey("atwater_mismatch").WhoseValue.Should().Be(1);
            (await db.Ingredients.AnyAsync(i => i.FdcId == Mislabelled)).Should().BeFalse();

            var wine = await db.Ingredients.SingleAsync(i => i.FdcId == Wine);
            wine.Name.Should().Be(WineDescription);
            wine.CurationStatusId.Should().Be(9001, "SR Legacy lands pending unless --curated");
            wine.GramsPerMilliliter.Should().Be(0.9941m, "1 fl oz weighs 29.4 g");
            wine.ReferenceServingGrams.Should().Be(147m);
            var stored = await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == wine.Id && n.NutrientId == Alcohol);
            stored.Amount.Should().Be(10.3m);
            stored.MeasurementId.Should().Be(1);

            var spirit = await db.Ingredients.SingleAsync(i => i.FdcId == Spirit);
            (await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == spirit.Id && n.NutrientId == Alcohol)).Amount.Should().Be(33.4m);
        }

        [Fact]
        public async Task Without_an_alcohol_nutrient_the_wine_is_still_accepted_and_its_alcohol_is_not_stored()
        {
            using var db = await NewContextAsync(withAlcoholNutrient: false);

            var report = await Importer(db).ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);

            report.Accepted.Should().Be(2);
            var wine = await db.Ingredients.SingleAsync(i => i.FdcId == Wine);
            (await db.Set<IngredientNutrientEntity>().Where(n => n.IngredientId == wine.Id).Select(n => n.NutrientId).ToListAsync())
                .Should().BeEquivalentTo(new[] { Calories, Protein, Carbs, Fat, Sugars });
        }
    }
}
