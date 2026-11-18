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
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Import.Services;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// --import-label-nutrition gives a food with no USDA record its manufacturer's label, per
    /// 100 g by the printed serving weight, keeps the label as an applied proposal, and writes
    /// nothing for a label without a gram weight, a food of another name or a USDA-linked food.
    /// </summary>
    public class LabelNutritionImportTests : IDisposable
    {
        private const long Gram = 1, Milligram = 8, Microgram = 9, Kcal = 16;
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, SatFat = 5001, Fiber = 5005,
            Sodium = 5021, Iron = 5024, VitaminD = 5010;
        private const string Header =
            "ingredient_id\tname\tbrand\tproduct\turl\tserving\tserving_grams\tcalories\tfat\tsaturated_fat\tsodium\tcarb\tfiber\tprotein\tvitamin_d\tiron\n";

        private readonly string _file = Path.Combine(Path.GetTempPath(), "labels-" + Guid.NewGuid().ToString("N") + ".tsv");

        public void Dispose() => File.Delete(_file);

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
                new NutrientEntity { Id = Sodium, Name = "Sodium", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = Iron, Name = "Iron", DefaultMeasurementId = Milligram },
                new NutrientEntity { Id = VitaminD, Name = "Vitamin D", DefaultMeasurementId = Microgram });
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, string? fdcId = null)
        {
            var food = new IngredientEntity { Name = name, FdcId = fdcId, CurationStatusId = 9003 };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static async Task<decimal?> AmountAsync(ApplicationDbContext db, long ingredientId, long nutrientId) =>
            (await db.Set<IngredientNutrientEntity>().SingleOrDefaultAsync(n => n.IngredientId == ingredientId && n.NutrientId == nutrientId))?.Amount;

        private LabelNutritionImportService Service(ApplicationDbContext db) =>
            new(db, NullLogger<LabelNutritionImportService>.Instance);

        [Fact]
        public async Task A_label_is_stored_per_100_g_by_its_serving_weight_and_kept_as_an_applied_proposal()
        {
            using var db = await NewContextAsync();
            var beans = await FoodAsync(db, "Black Beans");
            File.WriteAllText(_file, Header +
                $"{beans.Id}\tblack beans\tGoya\tBlack Beans 15.5 oz\thttps://shop.goya.com/products/goya-black-beans\t1/2 cup (122g)\t122\t130\t0.5\t0\t410\t23\t6\t8\t0\t2\n");

            var dry = await Service(db).ImportAsync(_file, dryRun: true);

            dry.Foods.Should().Be(1);
            dry.Rows.Should().Be(9);
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(0, "a dry run saves nothing");
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);

            var report = await Service(db).ImportAsync(_file, dryRun: false);

            report.Rows.Should().Be(9);
            (await AmountAsync(db, beans.Id, Calories)).Should().Be(106.56m);
            (await AmountAsync(db, beans.Id, Sodium)).Should().Be(336.07m);
            (await AmountAsync(db, beans.Id, Fiber)).Should().Be(4.92m);
            (await AmountAsync(db, beans.Id, VitaminD)).Should().Be(0m);
            (await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.NutrientId == Sodium)).MeasurementId.Should().Be(Milligram);
            (await db.Ingredients.SingleAsync(i => i.Id == beans.Id)).ReferenceServingGrams.Should().Be(122m);
            var kept = await db.FoodCatalogProposals.SingleAsync();
            kept.IngredientId.Should().Be(beans.Id);
            kept.Field.Should().Be(FoodCatalogReviewService.LabelNutritionField);
            kept.Source.Should().Be("label:manufacturer:shop.goya.com");
            kept.Status.Should().Be(FoodProposalStatus.Applied);
            kept.Batch.Should().Be(report.Batch);
            kept.ProposedValue.Should().Contain("\"servingGrams\":122").And.Contain("\"sodium\":410");
            ProposalPolicy.IsAllowed(kept.Field, kept.Source, out _).Should().BeTrue();
            ProposalPolicy.IsAllowed(kept.Field, "ai:qwen", out _).Should().BeFalse("a model may not author label values");

            var again = await Service(db).ImportAsync(_file, dryRun: false);

            again.Rows.Should().Be(0, "a rerun finds nothing missing");
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task A_value_the_label_does_not_print_stays_unknown_and_a_stored_value_is_kept()
        {
            using var db = await NewContextAsync();
            var chips = await FoodAsync(db, "Dark Chocolate Chips");
            db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = chips.Id, NutrientId = Calories, Amount = 500m, MeasurementId = Kcal });
            await db.SaveChangesAsync();
            File.WriteAllText(_file, Header +
                $"{chips.Id}\tDark Chocolate Chips\tNestle Toll House\tDark Chocolate Morsels\thttps://smartlabel.nestleusa.com/x\t1 Tbsp (14g)\t14\t80\t5\t3\t0\t8\t1\t\t0\t0.2\n");

            var report = await Service(db).ImportAsync(_file, dryRun: false);

            report.Foods.Should().Be(1);
            (await AmountAsync(db, chips.Id, Calories)).Should().Be(500m);
            (await AmountAsync(db, chips.Id, Protein)).Should().BeNull("the label prints less than 1 g, not a number");
            (await AmountAsync(db, chips.Id, Fat)).Should().Be(35.71m);
            (await AmountAsync(db, chips.Id, Iron)).Should().Be(1.43m);
        }

        [Fact]
        public async Task Labels_that_cannot_be_trusted_write_nothing()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            var oats = await FoodAsync(db, "Rolled Oats");
            var garlic = await FoodAsync(db, "Garlic", "1104647");
            var glaze = await FoodAsync(db, "Balsamic Glaze");
            File.WriteAllText(_file, Header +
                $"{wine.Id}\tDry White Wine\tBrand\tPinot Grigio\thttps://example.com/wine\t5 fl oz\t\t120\t0\t0\t0\t4\t0\t0\t0\t0\n" +
                $"{oats.Id}\tSteel Cut Oats\tBrand\tOats\thttps://example.com/oats\t1/2 cup (49g)\t49\t190\t3\t1\t0\t34\t4\t6\t0\t3\n" +
                $"{garlic.Id}\tGarlic\tBrand\tGarlic\thttps://example.com/garlic\t1 tsp (5g)\t5\t5\t0\t0\t0\t1\t0\t0\t0\t0\n" +
                $"{glaze.Id}\tBalsamic Glaze\tBrand\tGlaze\thttps://example.com/glaze\t1 Tbsp (19g)\t19\t300\t0\t0\t5\t9\t0\t0\t0\t0\n" +
                $"999999\tNo Such Food\tBrand\tX\thttps://example.com/x\t1 oz (28g)\t28\t100\t1\t0\t0\t10\t0\t1\t0\t0\n");

            var report = await Service(db).ImportAsync(_file, dryRun: false);

            report.Foods.Should().Be(0);
            report.SkippedByReason.Should().ContainKeys("serving_weight_missing", "name_differs", "has_usda_record", "food_not_found");
            report.SkippedByReason.Keys.Should().Contain(k => k.Contains("calories_impossible"), "1,579 kcal per 100 g is not a food");
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(0);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);
        }
    }
}
