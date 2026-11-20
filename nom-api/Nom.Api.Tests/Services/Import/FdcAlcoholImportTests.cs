using System;
using System.Collections.Generic;
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
    /// SR Legacy wine and spirits import once the gate counts their alcohol (FDC nutrient 1018),
    /// the alcohol is stored when the catalog has an Alcohol nutrient, an import can be limited
    /// to listed FDC ids, rehearsed and taken back, a retired food's FDC id counts as existing,
    /// and --attach-fdc gives a named catalog food a named USDA food in place, journaled and undoable.
    /// </summary>
    public class FdcAlcoholImportTests : IDisposable
    {
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, Sugars = 5036, Alcohol = 5038;
        private const string Wine = "174837", Spirit = "174815", Mislabelled = "999001";
        private const string WineDescription = "Alcoholic beverage, wine, table, white";
        private const string SrLegacy = FdcFoundationImportService.SrLegacyFood;

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

        private static FoodCatalogReviewService Review(ApplicationDbContext db) => new(db, null!, null!);

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, string? fdcId = null, bool deleted = false)
        {
            var food = new IngredientEntity { Name = name, FdcId = fdcId, CurationStatusId = 9003, IsDeleted = deleted };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static FdcFoundationImportService.AttachRequest[] Pair(IngredientEntity food, string fdcId = Wine, string? name = null) =>
            new[] { new FdcFoundationImportService.AttachRequest(food.Id, name ?? food.Name, fdcId) };

        [Fact]
        public async Task Wine_and_spirits_import_with_their_alcohol_and_a_food_alcohol_does_not_explain_is_rejected()
        {
            using var db = await NewContextAsync();

            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy);

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

            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy);

            report.Accepted.Should().Be(2);
            var wine = await db.Ingredients.SingleAsync(i => i.FdcId == Wine);
            (await db.Set<IngredientNutrientEntity>().Where(n => n.IngredientId == wine.Id).Select(n => n.NutrientId).ToListAsync())
                .Should().BeEquivalentTo(new[] { Calories, Protein, Carbs, Fat, Sugars });
        }

        [Fact]
        public async Task An_import_limited_to_listed_ids_reads_only_those_foods()
        {
            using var db = await NewContextAsync();

            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy, onlyFdcIds: new HashSet<string> { Wine, "170000", "1" });

            report.TotalFoundation.Should().Be(1);
            report.Accepted.Should().Be(1);
            report.Rejected.Should().Be(0);
            report.ListedNotInFile.Should().Equal("1", "170000");
            (await db.Ingredients.Select(i => i.FdcId).ToListAsync()).Should().Equal(Wine);
        }

        [Fact]
        public async Task A_retired_food_that_holds_the_fdc_id_counts_as_existing_and_is_left_alone()
        {
            using var db = await NewContextAsync();
            var retired = await FoodAsync(db, "White wine (deleted #7)", Wine, deleted: true);

            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy, onlyFdcIds: new HashSet<string> { Wine });

            report.Accepted.Should().Be(0);
            report.SkippedExisting.Should().Be(1);
            report.SkippedRetired.Should().Be(1);
            report.BackfilledNutrientRows.Should().Be(0);
            (await db.Ingredients.IgnoreQueryFilters().CountAsync()).Should().Be(1);
            (await db.Set<IngredientNutrientEntity>().IgnoreQueryFilters().CountAsync(n => n.IngredientId == retired.Id)).Should().Be(0);
        }

        [Fact]
        public async Task A_dry_run_import_reports_the_same_counts_and_saves_nothing()
        {
            using var db = await NewContextAsync();

            var dry = await Importer(db).ImportAsync(_dir, dataType: SrLegacy, dryRun: true);

            dry.DryRun.Should().BeTrue();
            dry.Accepted.Should().Be(2);
            dry.Rejected.Should().Be(1);
            dry.NutrientRows.Should().Be(11);
            dry.WithDensity.Should().Be(1);
            (await db.Ingredients.CountAsync()).Should().Be(0);
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(0);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);

            var real = await Importer(db).ImportAsync(_dir, dataType: SrLegacy);

            real.NutrientRows.Should().Be(dry.NutrientRows);
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(11);
        }

        [Fact]
        public async Task An_import_run_is_journaled_and_can_be_taken_back_by_its_stamp()
        {
            using var db = await NewContextAsync();
            var existing = await FoodAsync(db, "House spirit", Spirit);
            db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = existing.Id, NutrientId = Calories, Amount = 231m, MeasurementId = 16 });
            await db.SaveChangesAsync();

            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy);

            report.Accepted.Should().Be(1);
            report.BackfilledNutrientRows.Should().Be(4, "protein, carbohydrate, fat and alcohol were missing on the existing food");
            var wine = await db.Ingredients.SingleAsync(i => i.FdcId == Wine);
            wine.CreatedDate.Should().Be(report.RunStamp);
            var journal = await db.AuditLogEntries.Where(a => a.ChangeType == FdcFoundationImportService.ImportChangeType).ToListAsync();
            journal.Should().HaveCount(2).And.OnlyContain(a => a.Timestamp == report.RunStamp && a.EntityType == "Ingredient");
            journal.Should().ContainSingle(a => a.EntityId == wine.Id && a.PropertyName == nameof(IngredientEntity.FdcId) && a.NewValue == Wine);
            journal.Should().ContainSingle(a => a.EntityId == existing.Id && a.PropertyName == nameof(IngredientEntity.IngredientNutrients) && a.NewValue == "4");

            var dry = await Importer(db).UndoImportAsync(report.RunStamp, dryRun: true);

            dry.FoodsRetired.Should().Be(1);
            dry.NutrientRowsRemoved.Should().Be(4);
            (await db.Ingredients.CountAsync()).Should().Be(2, "a dry run saves nothing");

            var undone = await Importer(db).UndoImportAsync(report.RunStamp, dryRun: false);

            undone.FoodsRetired.Should().Be(1);
            undone.NutrientRowsRemoved.Should().Be(4);
            (await db.Ingredients.Select(i => i.Id).ToListAsync()).Should().Equal(existing.Id);
            (await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == wine.Id)).FdcId.Should().Be(Wine, "a retired food keeps its FDC id");
            (await db.Set<IngredientNutrientEntity>().Where(n => n.IngredientId == existing.Id).Select(n => n.NutrientId).ToListAsync()).Should().Equal(Calories);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
            (await Importer(db).ImportAsync(_dir, dataType: SrLegacy, onlyFdcIds: new HashSet<string> { Wine })).SkippedRetired.Should().Be(1);
        }

        [Fact]
        public async Task Undoing_an_import_keeps_a_food_a_recipe_line_already_uses()
        {
            using var db = await NewContextAsync();
            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy);
            var wine = await db.Ingredients.SingleAsync(i => i.FdcId == Wine);
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = 1, IngredientId = wine.Id, Quantity = 0.5m });
            await db.SaveChangesAsync();

            var undone = await Importer(db).UndoImportAsync(report.RunStamp, dryRun: false);

            undone.FoodsRetired.Should().Be(1);
            undone.KeptInUse.Should().Be(1);
            (await db.Ingredients.Select(i => i.FdcId).ToListAsync()).Should().Equal(Wine);
            (await db.AuditLogEntries.SingleAsync()).EntityId.Should().Be(wine.Id, "its journal entry stays so a later undo can retire it");
        }

        [Fact]
        public async Task A_named_food_is_offered_the_named_usda_food_once_and_a_dry_run_writes_nothing()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");

            var dry = await Importer(db).AttachAsync(_dir, Pair(wine, name: "dry white wine"), dryRun: true, dataType: SrLegacy);

            dry.Proposals.Should().Be(1);
            dry.Lines.Should().Equal($"attach   #{wine.Id} Dry White Wine <- fdc:{Wine} {WineDescription}: 6 values, 0.9941 g/ml, serving 147 g");
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);

            var report = await Importer(db).AttachAsync(_dir, Pair(wine), dryRun: false, dataType: SrLegacy);
            var again = await Importer(db).AttachAsync(_dir, Pair(wine), dryRun: false, dataType: SrLegacy);

            report.Proposals.Should().Be(1);
            again.Proposals.Should().Be(0);
            again.SkippedByReason.Should().ContainKey("already_proposed");
            var proposal = await db.FoodCatalogProposals.SingleAsync();
            proposal.Should().Match<FoodCatalogProposalEntity>(p =>
                p.IngredientId == wine.Id && p.Field == FdcFoundationImportService.AttachField && p.FdcId == Wine && p.Source == $"fdc:{Wine}"
                && p.Status == FoodProposalStatus.Pending && p.Batch == FdcFoundationImportService.AttachBatch && p.CurrentValue == "Dry White Wine");
            proposal.Reason.Should().StartWith("Named by the operator: USDA sr_legacy_food food 174837 (Alcoholic beverage, wine, table, white)");
            proposal.ProposedValue.Should().Contain($"\"description\":\"{WineDescription}\"")
                .And.Contain("\"fdcDataType\":\"sr_legacy_food\"")
                .And.Contain("\"gramsPerMilliliter\":0.9941")
                .And.Contain("\"referenceServingGrams\":147")
                .And.Contain($"[{Alcohol},10.3,1]");
            ProposalPolicy.IsAllowed(proposal.Field, proposal.Source, out _).Should().BeTrue();
            (await db.Ingredients.SingleAsync()).FdcId.Should().BeNull("the proposal changes nothing until it is applied");
        }

        [Fact]
        public async Task A_request_that_cannot_be_trusted_writes_no_proposal()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            var red = await FoodAsync(db, "Dry Red Wine");
            var linked = await FoodAsync(db, "Vodka", "555");
            var attached = await FoodAsync(db, "Gin", Spirit);
            var sherry = await FoodAsync(db, "Sherry");
            var port = await FoodAsync(db, "Port");
            var rum = await FoodAsync(db, "Rum");
            await FoodAsync(db, "Old wine (deleted #3)", "777", deleted: true);
            var requests = new[]
            {
                new FdcFoundationImportService.AttachRequest(wine.Id, "White Wine", Wine),
                new FdcFoundationImportService.AttachRequest(999999, "No Such Food", Wine),
                new FdcFoundationImportService.AttachRequest(linked.Id, "Vodka", Wine),
                new FdcFoundationImportService.AttachRequest(attached.Id, "Gin", Spirit),
                new FdcFoundationImportService.AttachRequest(red.Id, "Dry Red Wine", Spirit),
                new FdcFoundationImportService.AttachRequest(sherry.Id, "Sherry", "777"),
                new FdcFoundationImportService.AttachRequest(port.Id, "Port", "170000"),
                new FdcFoundationImportService.AttachRequest(rum.Id, "Rum", Mislabelled),
            };

            var report = await Importer(db).AttachAsync(_dir, requests, dryRun: false, dataType: SrLegacy);

            report.Requests.Should().Be(8);
            report.Proposals.Should().Be(0);
            report.SkippedByReason.Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["name_differs"] = 1,
                ["food_not_found"] = 1,
                ["has_usda_record"] = 1,
                ["already_attached"] = 1,
                ["fdc_id_taken"] = 2,
                ["fdc_food_not_in_file"] = 1,
                ["atwater_mismatch"] = 1,
            });
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task One_usda_food_is_offered_to_one_catalog_food_only()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            var other = await FoodAsync(db, "Cooking Wine");
            var requests = Pair(wine).Concat(Pair(other)).ToArray();

            var report = await Importer(db).AttachAsync(_dir, requests, dryRun: false, dataType: SrLegacy);
            var later = await Importer(db).AttachAsync(_dir, Pair(other), dryRun: false, dataType: SrLegacy);

            report.Proposals.Should().Be(1);
            report.SkippedByReason.Should().ContainKey("fdc_id_taken");
            later.SkippedByReason.Should().ContainKey("fdc_id_taken", "the pending proposal already promises the id to the first food");
            (await db.FoodCatalogProposals.SingleAsync()).IngredientId.Should().Be(wine.Id);
        }

        [Fact]
        public async Task Applying_the_attach_fills_what_is_missing_in_place_and_journals_every_change()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Calories, Amount = 80m, MeasurementId = 16 });
            await db.SaveChangesAsync();
            await Importer(db).AttachAsync(_dir, Pair(wine), dryRun: false, dataType: SrLegacy);
            var proposal = await db.FoodCatalogProposals.SingleAsync();

            (await Review(db).ApplyProposalAsync(proposal.Id, reviewerPersonId: 7)).Should().BeTrue();

            var stored = await db.Ingredients.SingleAsync(i => i.Id == wine.Id);
            stored.Name.Should().Be("Dry White Wine");
            stored.FdcId.Should().Be(Wine);
            stored.FdcDataType.Should().Be(SrLegacy);
            stored.CurationStatusId.Should().Be(9003);
            stored.GramsPerMilliliter.Should().Be(0.9941m);
            stored.ReferenceServingGrams.Should().Be(147m);
            stored.GramsPerItem.Should().BeNull();
            var rows = await db.Set<IngredientNutrientEntity>().Where(n => n.IngredientId == wine.Id).ToListAsync();
            rows.Should().HaveCount(6);
            rows.Single(n => n.NutrientId == Calories).Should().Match<IngredientNutrientEntity>(n => n.Amount == 80m && n.FdcId == null, "a stored value is never overwritten");
            rows.Where(n => n.NutrientId != Calories).Should().OnlyContain(n => n.FdcId == Wine);
            rows.Single(n => n.NutrientId == Alcohol).Amount.Should().Be(10.3m);
            (await db.IngredientAliases.SingleAsync()).Should().Match<IngredientAliasEntity>(a =>
                a.IngredientId == wine.Id && a.AliasName == WineDescription && a.SourceContext == IngredientLinkService.UsdaDescriptionContext);

            var journal = await db.AuditLogEntries.ToListAsync();
            journal.Should().OnlyContain(a => a.EntityType == FoodCatalogReviewService.FdcAttachEntityType && a.EntityId == wine.Id
                && a.ChangeType == FoodCatalogReviewService.FdcAttachChangeType && a.ChangedByPersonId == 7);
            journal.Select(a => (a.PropertyName, a.OldValue, a.NewValue)).Should().BeEquivalentTo(new (string?, string?, string?)[]
            {
                (nameof(IngredientEntity.FdcId), null, Wine),
                (nameof(IngredientEntity.FdcDataType), string.Empty, SrLegacy),
                (nameof(IngredientEntity.ReferenceServingGrams), null, "147"),
                (nameof(IngredientEntity.GramsPerMilliliter), null, "0.9941"),
                (nameof(IngredientEntity.Aliases), null, WineDescription),
            });
            (await db.FoodCatalogProposals.SingleAsync()).Status.Should().Be(FoodProposalStatus.Applied);
        }

        [Fact]
        public async Task A_same_name_attach_adds_no_alias_and_a_proposal_in_the_older_shape_still_applies()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, WineDescription);
            var honey = await FoodAsync(db, "Honey");
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = honey.Id, Field = FoodCatalogReviewService.FdcAttachField, FdcId = "169640", Source = "fdc:169640",
                ProposedValue = "{\"fdcDataType\":\"sr_legacy_food\",\"nutrients\":[{\"nutrientId\":5035,\"amount\":304,\"measurementId\":16}]}",
                Status = FoodProposalStatus.Pending,
            });
            await db.SaveChangesAsync();

            var report = await Importer(db).ImportAsync(_dir, dataType: SrLegacy, onlyFdcIds: new HashSet<string> { Wine });

            report.AttachProposals.Should().Be(1);
            foreach (var id in await db.FoodCatalogProposals.Select(p => p.Id).ToListAsync())
                (await Review(db).ApplyProposalAsync(id, reviewerPersonId: 1)).Should().BeTrue();

            (await db.Ingredients.SingleAsync(i => i.Id == wine.Id)).FdcId.Should().Be(Wine);
            (await db.IngredientAliases.CountAsync()).Should().Be(0);
            (await db.Set<IngredientNutrientEntity>().SingleAsync(n => n.IngredientId == honey.Id)).Should().Match<IngredientNutrientEntity>(n =>
                n.NutrientId == Calories && n.Amount == 304m && n.FdcId == "169640");
        }

        [Fact]
        public async Task An_attach_is_not_applied_when_another_food_holds_the_fdc_id()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            await Importer(db).AttachAsync(_dir, Pair(wine), dryRun: false, dataType: SrLegacy);
            await FoodAsync(db, "White wine (deleted #9)", Wine, deleted: true);
            var proposal = await db.FoodCatalogProposals.SingleAsync();

            (await Review(db).ApplyProposalAsync(proposal.Id, reviewerPersonId: 1)).Should().BeFalse();

            (await db.Ingredients.SingleAsync(i => i.Id == wine.Id)).FdcId.Should().BeNull();
            (await db.Set<IngredientNutrientEntity>().CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task An_applied_attach_is_taken_back_from_its_journal_and_can_be_made_again()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Calories, Amount = 80m, MeasurementId = 16 });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = 1, IngredientId = wine.Id, Quantity = 0.5m });
            await db.SaveChangesAsync();
            await Importer(db).AttachAsync(_dir, Pair(wine), dryRun: false, dataType: SrLegacy);
            await Review(db).ApplyProposalAsync((await db.FoodCatalogProposals.SingleAsync()).Id, reviewerPersonId: 7);

            var dry = await Importer(db).UndoAttachAsync(Pair(wine), dryRun: true);

            dry.Undone.Should().Be(1);
            dry.Lines.Single().Should().StartWith($"undone   #{wine.Id} Dry White Wine: fdc:{Wine} detached, 5 values removed");
            (await db.Ingredients.SingleAsync(i => i.Id == wine.Id)).FdcId.Should().Be(Wine, "a dry run saves nothing");

            var undone = await Importer(db).UndoAttachAsync(Pair(wine), dryRun: false);

            undone.Undone.Should().Be(1);
            var stored = await db.Ingredients.SingleAsync(i => i.Id == wine.Id);
            stored.FdcId.Should().BeNull();
            stored.FdcDataType.Should().BeEmpty();
            stored.GramsPerMilliliter.Should().BeNull();
            stored.ReferenceServingGrams.Should().BeNull();
            stored.CurationStatusId.Should().Be(9003);
            (await db.Set<IngredientNutrientEntity>().IgnoreQueryFilters().Where(n => n.IngredientId == wine.Id).Select(n => n.NutrientId).ToListAsync())
                .Should().Equal(new[] { Calories }, "only the rows the attach added are removed");
            (await db.IngredientAliases.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
            (await db.FoodCatalogProposals.SingleAsync()).Status.Should().Be(FoodProposalStatus.Rejected);
            (await db.RecipeIngredients.SingleAsync()).LastModifiedDate.Should().NotBeNull("the nutrition sweep recalculates the recipe");
            (await Importer(db).UndoAttachAsync(Pair(wine), dryRun: false)).SkippedByReason.Should().ContainKey("not_attached");

            var again = await Importer(db).AttachAsync(_dir, Pair(wine), dryRun: false, dataType: SrLegacy);

            again.Proposals.Should().Be(1, "a rejected proposal does not block the operator from naming the pair again");
        }

        [Fact]
        public async Task Undoing_an_attach_leaves_a_value_edited_since_and_rejects_a_proposal_still_pending()
        {
            using var db = await NewContextAsync();
            var wine = await FoodAsync(db, "Dry White Wine");
            var red = await FoodAsync(db, "Dry Red Wine");
            await Importer(db).AttachAsync(_dir, Pair(wine).Concat(Pair(red, Spirit)).ToArray(), dryRun: false, dataType: SrLegacy);
            var applied = await db.FoodCatalogProposals.SingleAsync(p => p.IngredientId == wine.Id);
            await Review(db).ApplyProposalAsync(applied.Id, reviewerPersonId: 7);
            (await db.Ingredients.SingleAsync(i => i.Id == wine.Id)).GramsPerMilliliter = 0.99m;
            await db.SaveChangesAsync();

            var undone = await Importer(db).UndoAttachAsync(Pair(wine).Concat(Pair(red, Spirit)).ToArray(), dryRun: false);

            undone.Undone.Should().Be(2);
            var stored = await db.Ingredients.SingleAsync(i => i.Id == wine.Id);
            stored.FdcId.Should().BeNull();
            stored.GramsPerMilliliter.Should().Be(0.99m, "a value changed after the attach is not overwritten");
            (await db.FoodCatalogProposals.Select(p => p.Status).ToListAsync()).Should().OnlyContain(s => s == FoodProposalStatus.Rejected);
        }

        [Fact]
        public void The_pairs_file_is_read_by_its_header()
        {
            var file = Path.Combine(_dir, "attach.tsv");
            File.WriteAllText(file, "fdc_id\tingredient_id\tname\n174837\t164\tDry White Wine\n\n\tx\tNo id\n");

            FdcFoundationImportService.ReadAttachRequests(file).Should().Equal(new FdcFoundationImportService.AttachRequest(164, "Dry White Wine", "174837"));
        }
    }
}
