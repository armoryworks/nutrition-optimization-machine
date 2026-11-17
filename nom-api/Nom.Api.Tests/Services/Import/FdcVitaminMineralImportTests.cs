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
    /// --backfill-nutrients also fills the vitamins and minerals that carry a Daily Value, and
    /// --supplement-nutrients fills what a Foundation food did not analyse from the SR Legacy food
    /// USDA lists under the same NDB number, without touching the macros or a stored value.
    /// </summary>
    public class FdcVitaminMineralImportTests : IDisposable
    {
        private const long Gram = 1, Milligram = 8, Microgram = 9, Kcal = 16;
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, SatFat = 5001, Fiber = 5005,
            VitaminA = 5008, VitaminC = 5009, Folate = 5017, VitaminB12 = 5018, Zinc = 5027, Copper = 5029;

        private readonly string _root = Path.Combine(Path.GetTempPath(), "fdc-micro-" + Guid.NewGuid().ToString("N"));
        private readonly string _foundation;
        private readonly string _sr;

        public FdcVitaminMineralImportTests()
        {
            _foundation = Path.Combine(_root, "foundation");
            _sr = Path.Combine(_root, "sr");
            Directory.CreateDirectory(_foundation);
            Directory.CreateDirectory(_sr);
            File.WriteAllText(Path.Combine(_foundation, "foundation_food.csv"),
                "\"fdc_id\",\"NDB_number\",\"footnote\"\n" +
                "\"1104647\",\"11215\",\"\"\n" +
                "\"790646\",\"100253\",\"\"\n" +
                "\"1999626\",\"11265\",\"\"\n");
            File.WriteAllText(Path.Combine(_foundation, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n" +
                "\"1104647\",\"foundation_food\",\"Garlic, raw\",\"11\",\"2020-10-30\"\n" +
                "\"790646\",\"foundation_food\",\"Onions, yellow, raw\",\"11\",\"2020-04-01\"\n" +
                "\"1999626\",\"foundation_food\",\"Mushroom, portabella\",\"11\",\"2021-10-28\"\n" +
                "\"334247\",\"foundation_food\",\"Sugars, granulated\",\"19\",\"2019-04-01\"\n" +
                "\"330001\",\"sample_food\",\"Sugars, powdered\",\"19\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_foundation, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n" +
                "\"1\",\"1104647\",\"1008\",\"143\"\n" +
                "\"2\",\"1104647\",\"1003\",\"6.62\"\n" +
                "\"3\",\"1104647\",\"1005\",\"28.2\"\n" +
                "\"4\",\"1104647\",\"1004\",\"0.38\"\n" +
                "\"5\",\"1104647\",\"1095\",\"1.06\"\n" +
                "\"6\",\"1104647\",\"1177\",\"3\"\n" +
                "\"7\",\"1104647\",\"1104\",\"9\"\n");
            File.WriteAllText(Path.Combine(_sr, "sr_legacy_food.csv"),
                "\"fdc_id\",\"NDB_number\"\n" +
                "\"169230\",\"11215\"\n" +
                "\"168436\",\"11265\"\n");
            File.WriteAllText(Path.Combine(_sr, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n" +
                "\"169230\",\"sr_legacy_food\",\"Garlic, raw\",\"11\",\"2019-04-01\"\n" +
                "\"168436\",\"sr_legacy_food\",\"Mushrooms, portabella, grilled\",\"11\",\"2019-04-01\"\n" +
                "\"169655\",\"sr_legacy_food\",\"Sugars, granulated\",\"19\",\"2019-04-01\"\n" +
                "\"169656\",\"sr_legacy_food\",\"Sugars, powdered\",\"19\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_sr, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n" +
                "\"1\",\"169230\",\"1008\",\"149\"\n" +
                "\"2\",\"169230\",\"1003\",\"6.36\"\n" +
                "\"3\",\"169230\",\"1005\",\"33.06\"\n" +
                "\"4\",\"169230\",\"1004\",\"0.5\"\n" +
                "\"5\",\"169230\",\"1258\",\"2.089\"\n" +
                "\"6\",\"169230\",\"1079\",\"2.1\"\n" +
                "\"7\",\"169230\",\"1162\",\"31.2\"\n" +
                "\"8\",\"169230\",\"1190\",\"3\"\n" +
                "\"9\",\"169230\",\"1178\",\"0\"\n" +
                "\"10\",\"169230\",\"1095\",\"1.16\"\n" +
                "\"11\",\"169230\",\"1098\",\"299\"\n" +
                "\"12\",\"169230\",\"1106\",\"0\"\n" +
                "\"13\",\"168436\",\"1162\",\"0\"\n" +
                "\"14\",\"169655\",\"1162\",\"0\"\n" +
                "\"15\",\"169656\",\"1162\",\"0\"\n");
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private static async Task<ApplicationDbContext> NewContextAsync()
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
                new NutrientEntity { Id = Fiber, Name = "Dietary Fiber", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = VitaminA, Name = "Vitamin A", DefaultMeasurementId = Microgram },
                new NutrientEntity { Id = VitaminC, Name = "Vitamin C", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = Folate, Name = "Folate", DefaultMeasurementId = Microgram },
                new NutrientEntity { Id = VitaminB12, Name = "Vitamin B12", DefaultMeasurementId = Microgram },
                new NutrientEntity { Id = Zinc, Name = "Zinc", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = Copper, Name = "Copper", DefaultMeasurementId = Milligram });
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, string fdcId)
        {
            var food = new IngredientEntity { Name = name, FdcId = fdcId, CurationStatusId = 9003 };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static async Task<IngredientNutrientEntity?> RowAsync(ApplicationDbContext db, long ingredientId, long nutrientId) =>
            await db.Set<IngredientNutrientEntity>().SingleOrDefaultAsync(n => n.IngredientId == ingredientId && n.NutrientId == nutrientId);

        [Fact]
        public async Task Backfill_fills_vitamins_and_minerals_and_leaves_out_quantities_in_another_definition()
        {
            using var db = await NewContextAsync();
            var garlic = await FoodAsync(db, "Garlic", "1104647");

            var report = await new FdcNutrientBackfillService(db, NullLogger<FdcNutrientBackfillService>.Instance)
                .BackfillAsync(_foundation, dryRun: false);

            report.RowsByNutrient.Should().ContainKey("zinc");
            var zinc = await RowAsync(db, garlic.Id, Zinc);
            zinc!.Amount.Should().Be(1.06m);
            zinc.MeasurementId.Should().Be(Milligram);
            zinc.FdcId.Should().BeNull();
            (await RowAsync(db, garlic.Id, Folate)).Should().BeNull("total folate is not the DFE the label counts");
            (await RowAsync(db, garlic.Id, VitaminA)).Should().BeNull("vitamin A in IU is not RAE");
        }

        [Fact]
        public async Task Supplement_pairs_a_foundation_food_without_an_ndb_number_by_its_exact_description()
        {
            using var db = await NewContextAsync();
            var sugar = await FoodAsync(db, "Sugar", "334247");
            var sample = await FoodAsync(db, "Powdered Sugar", "330001");

            var report = await new FdcNutrientBackfillService(db, NullLogger<FdcNutrientBackfillService>.Instance)
                .SupplementFromSrLegacyAsync(_foundation, _sr, dryRun: false);

            report.LinkedIngredients.Should().Be(1, "a sample record is not a Foundation food");
            var vitaminC = await RowAsync(db, sugar.Id, VitaminC);
            vitaminC!.Amount.Should().Be(0m);
            vitaminC.FdcId.Should().Be("169655");
            (await RowAsync(db, sample.Id, VitaminC)).Should().BeNull();
        }

        [Fact]
        public async Task Supplement_fills_what_the_foundation_food_lacks_from_the_sr_legacy_food_of_the_same_ndb_number()
        {
            using var db = await NewContextAsync();
            var garlic = await FoodAsync(db, "Garlic", "1104647");
            var onion = await FoodAsync(db, "Yellow Onion", "790646");
            var portabella = await FoodAsync(db, "Portabella Mushrooms", "1999626");
            var backfill = new FdcNutrientBackfillService(db, NullLogger<FdcNutrientBackfillService>.Instance);
            await backfill.BackfillAsync(_foundation, dryRun: false);
            var before = await db.Set<IngredientNutrientEntity>().CountAsync();

            var dry = await backfill.SupplementFromSrLegacyAsync(_foundation, _sr, dryRun: true);

            dry.LinkedIngredients.Should().Be(1, "yellow onion has no SR Legacy food under its NDB number");
            dry.PreparationDiffers.Should().Be(1, "the SR Legacy portabella is grilled, the Foundation one is not");
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(before, "a dry run saves nothing");

            var real = await backfill.SupplementFromSrLegacyAsync(_foundation, _sr, dryRun: false);

            real.Rows.Should().Be(dry.Rows);
            real.RowsByNutrient.Keys.Should().BeEquivalentTo(new[] { "fiber", "vitamin_c", "folate", "vitamin_b12", "vitamin_a" });
            var vitaminC = await RowAsync(db, garlic.Id, VitaminC);
            vitaminC!.Amount.Should().Be(31.2m);
            vitaminC.FdcId.Should().Be("169230", "the row names the SR Legacy food it came from");
            vitaminC.CreatedDate.Should().Be(real.RunStamp);
            (await RowAsync(db, garlic.Id, Folate))!.Amount.Should().Be(3m);
            (await RowAsync(db, garlic.Id, VitaminB12))!.Amount.Should().Be(0m, "a measured zero is a value");
            (await RowAsync(db, garlic.Id, Zinc))!.Amount.Should().Be(1.06m, "a stored value is never overwritten");
            (await RowAsync(db, garlic.Id, Calories))!.Amount.Should().Be(143m, "the macros stay the Foundation food's own");
            (await RowAsync(db, garlic.Id, Fat))!.Amount.Should().Be(0.38m);
            (await RowAsync(db, garlic.Id, SatFat)).Should().BeNull("2.089 g saturated fat cannot sit in the ingredient's own 0.38 g of fat");
            (await RowAsync(db, garlic.Id, Copper)).Should().BeNull();
            real.DroppedValuesByReason.Should().ContainKey("saturated_fat_exceeds_fat").And.ContainKey("copper_impossible");
            (await db.Set<IngredientNutrientEntity>().AnyAsync(n => n.IngredientId != garlic.Id && n.FdcId != null)).Should().BeFalse();
            (await RowAsync(db, portabella.Id, VitaminC)).Should().BeNull();
            (await db.Set<IngredientNutrientEntity>().AnyAsync(n => n.IngredientId == onion.Id)).Should().BeFalse();

            (await backfill.SupplementFromSrLegacyAsync(_foundation, _sr, dryRun: false)).Rows.Should().Be(0, "the supplement is idempotent");
        }
    }
}
