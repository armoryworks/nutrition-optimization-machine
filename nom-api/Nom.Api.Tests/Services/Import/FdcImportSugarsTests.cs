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
    /// SR Legacy foods import with their sugar values, and re-running an import only fills
    /// nutrients a food is missing — it never overwrites a value already stored.
    /// </summary>
    public class FdcImportSugarsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdc-test-" + Guid.NewGuid().ToString("N"));

        public FdcImportSugarsTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n" +
                "\"169655\",\"sr_legacy_food\",\"Sugars, granulated\",\"19\",\"2019-04-01\"\n" +
                "\"170000\",\"foundation_food\",\"Not this one\",\"19\",\"2019-04-01\"\n");
            File.WriteAllText(Path.Combine(_dir, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n" +
                "\"1\",\"169655\",\"1008\",\"387\"\n" +
                "\"2\",\"169655\",\"1003\",\"0\"\n" +
                "\"3\",\"169655\",\"1005\",\"99.98\"\n" +
                "\"4\",\"169655\",\"1004\",\"0\"\n" +
                "\"5\",\"169655\",\"2000\",\"99.8\"\n");
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
                new NutrientEntity { Id = 5002, Name = "Fat" },
                new NutrientEntity { Id = 5036, Name = "Total Sugars" },
                new NutrientEntity { Id = 5007, Name = "Added Sugars" });
            await db.SaveChangesAsync();
            return db;
        }

        [Fact]
        public async Task A_same_name_catalog_ingredient_gets_an_attach_proposal_that_fills_only_gaps()
        {
            using var db = await NewContextAsync();
            var seedSugar = new Nom.Data.Recipe.IngredientEntity { Name = "Sugars, granulated", CurationStatusId = 9003 };
            db.Ingredients.Add(seedSugar);
            await db.SaveChangesAsync();
            db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = seedSugar.Id, NutrientId = 5035, Amount = 400m, MeasurementId = 16 });
            await db.SaveChangesAsync();
            var importer = new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance);

            var report = await importer.ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);
            (await importer.ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood)).AttachProposals.Should().Be(0, "one proposal per ingredient");

            report.Accepted.Should().Be(0);
            report.AttachProposals.Should().Be(1);
            var proposal = await db.FoodCatalogProposals.SingleAsync();
            proposal.Should().Match<Nom.Data.Curation.FoodCatalogProposalEntity>(p =>
                p.IngredientId == seedSugar.Id && p.Field == FdcFoundationImportService.AttachField && p.Source == "fdc:169655");

            var review = new Nom.Orch.Services.FoodCatalogReviewService(db, null!, null!);
            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 1)).Should().BeTrue();

            var sugar = await db.Ingredients.SingleAsync(i => i.Id == seedSugar.Id);
            sugar.FdcId.Should().Be("169655");
            var facts = await db.Set<IngredientNutrientEntity>().Where(n => n.IngredientId == seedSugar.Id).ToListAsync();
            facts.Single(n => n.NutrientId == 5035).Amount.Should().Be(400m, "an existing value is never overwritten");
            facts.Single(n => n.NutrientId == 5036).Amount.Should().Be(99.8m);
        }

        [Fact]
        public async Task Sr_legacy_food_imports_with_total_sugars_and_reruns_never_overwrite()
        {
            using var db = await NewContextAsync();
            var importer = new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance);

            var first = await importer.ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);

            first.Accepted.Should().Be(1, "only sr_legacy_food rows are read");
            var sugar = await db.Ingredients.SingleAsync();
            sugar.FdcDataType.Should().Be(FdcFoundationImportService.SrLegacyFood);
            sugar.CurationStatusId.Should().Be(9001, "SR Legacy lands pending unless --curated");
            var sugars = await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == sugar.Id && n.NutrientId == 5036);
            sugars.Amount.Should().Be(99.8m);

            sugars.Amount = 42m;
            var protein = await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == sugar.Id && n.NutrientId == 5001);
            db.Remove(protein);
            await db.SaveChangesAsync();

            var second = await importer.ImportAsync(_dir, dataType: FdcFoundationImportService.SrLegacyFood);

            second.Accepted.Should().Be(0);
            second.SkippedExisting.Should().Be(1);
            second.BackfilledNutrientRows.Should().Be(1, "only the missing protein row is filled");
            (await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == sugar.Id && n.NutrientId == 5036)).Amount
                .Should().Be(42m, "an existing value is never overwritten");
        }
    }
}
