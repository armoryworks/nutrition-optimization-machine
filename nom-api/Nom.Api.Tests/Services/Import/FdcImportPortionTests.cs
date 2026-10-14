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
    /// An imported food keeps its reference serving (the portion a standalone food is planned in)
    /// apart from the weight of one item (what a recipe's "4 strawberries" weighs), both from
    /// USDA food_portion.csv.
    /// </summary>
    public class FdcImportPortionTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdc-portion-test-" + Guid.NewGuid().ToString("N"));

        public FdcImportPortionTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n" +
                "\"167762\",\"sr_legacy_food\",\"Strawberries, raw\",\"9\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_dir, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n" +
                "\"1\",\"167762\",\"1008\",\"32\"\n" +
                "\"2\",\"167762\",\"1003\",\"0.67\"\n" +
                "\"3\",\"167762\",\"1005\",\"7.68\"\n" +
                "\"4\",\"167762\",\"1004\",\"0.3\"\n");
            File.WriteAllText(Path.Combine(_dir, "food_portion.csv"),
                "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"portion_description\",\"modifier\",\"gram_weight\",\"data_points\",\"footnote\",\"min_year_acquired\"\n" +
                "\"81922\",\"167762\",\"4\",\"1\",\"9999\",\"\",\"cup, whole\",\"144\",\"\",\"\",\"\"\n" +
                "\"81925\",\"167762\",\"7\",\"1\",\"9999\",\"\",\"medium (1-1/4\"\" dia)\",\"12\",\"\",\"\",\"\"\n" +
                "\"81926\",\"167762\",\"8\",\"1\",\"9999\",\"\",\"NLEA serving\",\"147\",\"\",\"\",\"\"\n");
            File.WriteAllText(Path.Combine(_dir, "measure_unit.csv"), "\"id\",\"name\"\n\"9999\",\"undetermined\"\n");
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private static async Task<ApplicationDbContext> NewContextAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = 16, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 4 });
            db.Set<NutrientEntity>().AddRange(
                new NutrientEntity { Id = 5035, Name = "Calories" },
                new NutrientEntity { Id = 5001, Name = "Protein" },
                new NutrientEntity { Id = 5003, Name = "Carbohydrates" },
                new NutrientEntity { Id = 5002, Name = "Fat" });
            await db.SaveChangesAsync();
            return db;
        }

        [Fact]
        public async Task A_new_food_gets_one_item_weight_without_shrinking_its_reference_serving()
        {
            using var db = await NewContextAsync();

            var report = await new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance)
                .ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);

            report.Accepted.Should().Be(1);
            var strawberries = await db.Ingredients.SingleAsync();
            strawberries.GramsPerItem.Should().Be(12m);
            strawberries.ReferenceServingGrams.Should().Be(144m);
            strawberries.GramsPerMilliliter.Should().Be(0.6087m);
        }

        [Fact]
        public async Task An_attach_proposal_carries_the_item_weight()
        {
            using var db = await NewContextAsync();
            var seed = new Nom.Data.Recipe.IngredientEntity { Name = "Strawberries, raw", CurationStatusId = 9003 };
            db.Ingredients.Add(seed);
            await db.SaveChangesAsync();

            await new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance)
                .ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);
            var proposal = await db.FoodCatalogProposals.SingleAsync();
            (await new Nom.Orch.Services.FoodCatalogReviewService(db, null!, null!).ApplyProposalAsync(proposal.Id, reviewerPersonId: 1))
                .Should().BeTrue();

            var strawberries = await db.Ingredients.SingleAsync(i => i.Id == seed.Id);
            strawberries.GramsPerItem.Should().Be(12m);
            strawberries.ReferenceServingGrams.Should().Be(144m);
        }
    }
}
