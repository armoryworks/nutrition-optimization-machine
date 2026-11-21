using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Import.Services;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// The catalog admission pass (`--admit`) over USDA foods already in the catalog, and the
    /// importers that store the same rule's verdict as they create foods: SR Legacy facts from the
    /// 2018-04 release, admitted (prepared foods included, with no group), held, regrouped from the category alone,
    /// journaled, undone (the undo journaled and undone in turn), and stopped by its own gates
    /// before it writes.
    /// </summary>
    public class CatalogAdmissionTests : IDisposable
    {
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, Alcohol = 5038;
        private const string Sr = "sr_legacy_food";
        private const string Gamay = "171872", Vodka = "174818", Vodka100 = "173664", Burger = "170717", Pepper = "170931", Applesauce = "173512", Baobab = "167806", PotatoPancakes = "170092";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "catalog-admission-test-" + Guid.NewGuid().ToString("N"));
        private string SrDir => Path.Combine(_root, "sr");
        private string FoundationDir => Path.Combine(_root, "foundation");
        private string Checksums => Path.Combine(_root, "fdc-releases.sha256");

        private static readonly (string FdcId, string Description, int Category, decimal Kcal, decimal P, decimal C, decimal F, decimal? A, decimal? Fiber, string? Factors)[] Foods =
        {
            (Gamay, "Alcoholic Beverage, wine, table, red, Gamay", 14, 78m, 0.07m, 2.38m, 0m, 9.8m, null, "3.36,8.37,3.92"),
            (Vodka, "Alcoholic beverage, distilled, vodka, 80 proof", 14, 231m, 0m, 0m, 0m, 33.4m, 0m, null),
            (Vodka100, "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 100 proof", 14, 295m, 0m, 0m, 0m, 42.5m, 0m, null),
            (Burger, "McDONALD'S, Hamburger", 21, 264m, 12.92m, 30.28m, 10.09m, null, 1.3m, null),
            (Pepper, "Spices, pepper, black", 2, 251m, 10.39m, 63.95m, 3.26m, 0m, 25.3m, "1.83,8.37,3.2"),
            (Applesauce, "Babyfood, fruit, applesauce, strained", 3, 41m, 0.2m, 10.8m, 0.2m, 0m, 1.7m, null),
            (Baobab, "Baobab powder", 9, 250m, 3.69m, 79.65m, 0.47m, null, 44.5m, "3.36,8.37,3.6"),
            ("990001", "Beverages, test drink one", 14, 40m, 0m, 10m, 0m, null, 0m, null),
            ("990002", "Beverages, test drink two", 14, 42m, 0m, 10.5m, 0m, null, 0m, null),
            ("990003", "Beverages, test drink three", 14, 38m, 0m, 9.5m, 0m, null, 0m, null),
            ("990004", "Beverages, test syrup drink", 14, 300m, 0m, 75m, 0m, null, 0m, null),
            (PotatoPancakes, "Potato pancakes", 11, 268m, 6.08m, 27.81m, 14.76m, 0m, 3.3m, null),
            ("990011", "Vegetables, test greens one", 11, 30m, 2m, 6m, 0.2m, null, 2m, null),
            ("990012", "Vegetables, test greens two", 11, 31m, 2m, 6m, 0.2m, null, 2m, null),
            ("990013", "Vegetables, test greens three", 11, 29m, 2m, 6m, 0.2m, null, 2m, null),
        };

        public CatalogAdmissionTests()
        {
            Directory.CreateDirectory(SrDir);
            Directory.CreateDirectory(FoundationDir);
            File.WriteAllText(Path.Combine(SrDir, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n"
                + string.Concat(Foods.Select(f => $"\"{f.FdcId}\",\"{Sr}\",\"{f.Description}\",\"{f.Category}\",\"2019-04-01\"\n")));
            var id = 0;
            string Row(string fdc, int nutrient, decimal? amount) => amount is { } a ? $"\"{++id}\",\"{fdc}\",\"{nutrient}\",\"{a.ToString(CultureInfo.InvariantCulture)}\"\n" : string.Empty;
            File.WriteAllText(Path.Combine(SrDir, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n"
                + string.Concat(Foods.Select(f => Row(f.FdcId, 1008, f.Kcal) + Row(f.FdcId, 1003, f.P) + Row(f.FdcId, 1005, f.C) + Row(f.FdcId, 1004, f.F)
                    + Row(f.FdcId, 1018, f.A) + Row(f.FdcId, 1079, f.Fiber))));
            File.WriteAllText(Path.Combine(SrDir, "food_category.csv"),
                "\"id\",\"code\",\"description\"\n\"2\",\"0200\",\"Spices and Herbs\"\n\"3\",\"0300\",\"Baby Foods\"\n\"9\",\"0900\",\"Fruits and Fruit Juices\"\n\"11\",\"1100\",\"Vegetables and Vegetable Products\"\n"
                + "\"14\",\"1400\",\"Beverages\"\n\"21\",\"2100\",\"Fast Foods\"\n");
            var withFactors = Foods.Where(f => f.Factors != null).Select((f, i) => (f, i: 20000 + i)).ToList();
            File.WriteAllText(Path.Combine(SrDir, "food_nutrient_conversion_factor.csv"),
                "\"id\",\"fdc_id\"\n" + string.Concat(withFactors.Select(x => $"\"{x.i}\",\"{x.f.FdcId}\"\n")));
            File.WriteAllText(Path.Combine(SrDir, "food_calorie_conversion_factor.csv"),
                "\"food_nutrient_conversion_factor_id\",\"protein_value\",\"fat_value\",\"carbohydrate_value\"\n"
                + string.Concat(withFactors.Select(x => $"\"{x.i}\",\"{string.Join("\",\"", x.f.Factors!.Split(','))}\"\n")));
            File.WriteAllText(Path.Combine(FoundationDir, "food.csv"), "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n");
            File.WriteAllText(Path.Combine(FoundationDir, "food_nutrient.csv"), "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n");
            RecordChecksums();
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private void RecordChecksums() =>
            File.WriteAllLines(Checksums, new[] { "# test release" }.Concat(Directory.GetFiles(_root, "*.csv", SearchOption.AllDirectories)
                .Select(f => $"{CatalogAdmissionService.Sha256(f)}  {Path.GetRelativePath(_root, f)}")));

        private static ApplicationDbContext NewContext()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = 16, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 4 });
            db.Set<NutrientEntity>().AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = 16 },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Carbs, Name = "Total Carbohydrates", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Fat, Name = "Fat", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = 1 });
            db.SaveChanges();
            return db;
        }

        private static IngredientEntity Food(ApplicationDbContext db, string fdcId, CurationStatusEnum status, FoodGroupEnum? group = null, string? name = null)
        {
            var usda = Foods.Single(f => f.FdcId == fdcId);
            var food = new IngredientEntity
            {
                Name = name ?? usda.Description, FdcId = fdcId, FdcDataType = Sr, CurationStatusId = (long)status,
                FoodGroupId = (long?)group, CreatedDate = DateTime.UtcNow,
            };
            db.Ingredients.Add(food);
            db.SaveChanges();
            db.Set<IngredientNutrientEntity>().AddRange(
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Calories, Amount = usda.Kcal, MeasurementId = 16 },
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Protein, Amount = usda.P, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Carbs, Amount = usda.C, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Fat, Amount = usda.F, MeasurementId = 1 });
            if (usda.A is { } a)
                db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Alcohol, Amount = a, MeasurementId = 1 });
            db.SaveChanges();
            return food;
        }

        private sealed record Seeded(long Gamay, long Vodka, long Burger, long Pepper, long Applesauce, long Pick);

        private static Seeded Seed(ApplicationDbContext db)
        {
            var gamay = Food(db, Gamay, CurationStatusEnum.PendingCuration);
            var vodka = Food(db, Vodka, CurationStatusEnum.PendingCuration, FoodGroupEnum.Beverages);
            var burger = Food(db, Burger, CurationStatusEnum.PendingCuration, FoodGroupEnum.ProteinFoods);
            var pepper = Food(db, Pepper, CurationStatusEnum.Curated, FoodGroupEnum.Vegetables, name: "Pepper");
            var applesauce = Food(db, Applesauce, CurationStatusEnum.Curated, FoodGroupEnum.Fruits, name: "Applesauce");
            var wording = new IngredientEntity { Name = "butcher's meat", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.Add(wording);
            db.SaveChanges();
            var pick = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = wording.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = wording.Name,
                ProposedValue = burger.Id.ToString(CultureInfo.InvariantCulture), FdcId = Burger, Source = "ai:qwen2.5:14b-instruct", Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(pick);
            db.SaveChanges();
            return new Seeded(gamay.Id, vodka.Id, burger.Id, pepper.Id, applesauce.Id, pick.Id);
        }

        private CatalogAdmissionService Service(ApplicationDbContext db) => new(db, NullLogger<CatalogAdmissionService>.Instance);

        private CatalogAdmissionService.AdmitOptions Options(bool dryRun, int changeLimit = CatalogAdmissionService.MaxChangedFoods) =>
            new(FoundationDir, SrDir, null, Checksums, dryRun, changeLimit);

        private static IngredientEntity Get(ApplicationDbContext db, long id) => db.Ingredients.IgnoreQueryFilters().AsNoTracking().Single(i => i.Id == id);

        [Fact]
        public async Task A_dry_run_reports_what_the_rule_decides_and_writes_nothing()
        {
            using var db = NewContext();
            var s = Seed(db);

            var report = await Service(db).AdmitAsync(Options(dryRun: true));

            report.Stopped.Should().BeEmpty();
            report.Admitted.Should().Be(3, "Gamay, vodka and the hamburger pass every check");
            report.AdmittedPrepared.Should().Be(1, "the hamburger is a Fast Foods food, admitted as a prepared food");
            report.Held.Should().Be(0);
            report.HeldByReason.Should().BeEmpty();
            report.KeptCurated.Should().Be(0, "curated applesauce in Baby Foods is a prepared food the rule admits too");
            report.GroupMoves.Should().ContainKey("Vegetables -> none").WhoseValue.Should().Be(1);
            report.GroupMoves.Should().ContainKey("ProteinFoods -> none").WhoseValue.Should().Be(1, "a prepared food has no food group");
            report.PicksWithdrawn.Should().Be(1, "the model pick at the hamburger points at a prepared food, which is never a link target");
            report.DescriptionsAdded.Should().Be(5);
            report.EnergyPassRate.Should().Be(1m);
            report.CategoryMissing.Should().Be(0);
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, s.Pepper).FoodGroupId.Should().Be((long)FoodGroupEnum.Vegetables);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
            (await db.IngredientAliases.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task An_apply_admits_prepared_food_without_a_group_regroups_and_journals_every_change_under_one_stamp()
        {
            using var db = NewContext();
            var s = Seed(db);

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().BeEmpty();
            report.SelfUndo.Should().BeNull();
            var gamay = Get(db, s.Gamay);
            gamay.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            gamay.FoodGroupId.Should().Be((long)FoodGroupEnum.Beverages);
            gamay.FdcCategoryId.Should().Be(14);
            gamay.FdcCategory.Should().Be("Beverages");
            gamay.EnergyCheck.Should().Be(EnergyCheckOutcome.OwnFactors);
            gamay.AdmissionReason.Should().BeNull();
            Get(db, s.Vodka).EnergyCheck.Should().Be(EnergyCheckOutcome.General);
            var burger = Get(db, s.Burger);
            burger.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "a prepared food is admitted so a household can plan it");
            burger.AdmissionReason.Should().BeNull();
            burger.FdcCategoryId.Should().Be(21);
            PreparedFoods.IsPrepared(burger).Should().BeTrue();
            burger.FoodGroupId.Should().BeNull("Fast Foods gives no group");
            var pepper = Get(db, s.Pepper);
            pepper.FoodGroupId.Should().BeNull("black pepper leaves Vegetables");
            pepper.IsWholeFood.Should().BeFalse();
            pepper.EnergyCheck.Should().Be(EnergyCheckOutcome.ExemptSmall);
            Get(db, s.Applesauce).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            Get(db, s.Applesauce).AdmissionReason.Should().BeNull();
            Get(db, s.Applesauce).FoodGroupId.Should().BeNull("Baby Foods gives no group");

            (await db.IngredientAliases.Where(a => a.IngredientId == s.Pepper).Select(a => a.AliasName + "|" + a.SourceContext).SingleAsync())
                .Should().Be("Spices, pepper, black|" + IngredientLinkService.UsdaDescriptionContext);
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == s.Pick)).IsDeleted.Should().BeTrue("a prepared food is never a link target");

            var journal = await db.AuditLogEntries.ToListAsync();
            journal.Should().OnlyContain(a => a.Timestamp == report.RunStamp);
            journal.Single(a => a.ChangeType == CatalogAdmissionService.RunChangeType).NewValue.Should().Be(CatalogAdmissionRule.Version);
            journal.Single(a => a.EntityId == s.Gamay && a.ChangeType == CatalogAdmissionService.AdmitChangeType)
                .Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.PropertyName == "CurationStatusId" && a.OldValue == "9001" && a.NewValue == "9003");
            journal.Should().ContainSingle(a => a.EntityId == s.Burger && a.ChangeType == CatalogAdmissionService.AdmitChangeType && a.PropertyName == "CurationStatusId");
            journal.Single(a => a.EntityId == s.Pepper && a.ChangeType == CatalogAdmissionService.GroupChangeType && a.PropertyName == "FoodGroupId")
                .Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.OldValue == "3200" && a.NewValue == null);
            journal.Should().ContainSingle(a => a.ChangeType == CatalogAdmissionService.PickWithdrawChangeType && a.EntityId == s.Pick);
            journal.Should().NotContain(a => a.ChangeType == LineRelinkService.ProposalWithdrawChangeType, "the relink undo sweeps that type by time");
        }

        [Fact]
        public async Task An_open_model_pick_at_a_food_an_admin_rejected_is_withdrawn_and_the_undo_restores_it()
        {
            using var db = NewContext();
            var s = Seed(db);
            var burger = db.Ingredients.Single(i => i.Id == s.Burger);
            burger.CurationStatusId = (long)CurationStatusEnum.Rejected;
            await db.SaveChangesAsync();

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.PicksWithdrawn.Should().Be(1);
            Get(db, s.Burger).CurationStatusId.Should().Be((long)CurationStatusEnum.Rejected, "the pass never changes an admin's rejection");
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == s.Pick)).IsDeleted.Should().BeTrue();

            (await Service(db).UndoAsync(report.RunStamp, dryRun: false)).PicksRestored.Should().Be(1);
            (await db.FoodCatalogProposals.SingleAsync(p => p.Id == s.Pick)).IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task The_undo_restores_each_value_the_run_set_and_leaves_one_changed_since()
        {
            using var db = NewContext();
            var s = Seed(db);
            var report = await Service(db).AdmitAsync(Options(dryRun: false));
            var gamay = db.Ingredients.Single(i => i.Id == s.Gamay);
            gamay.FoodGroupId = (long)FoodGroupEnum.Dairy;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var rehearsal = await Service(db).UndoAsync(report.RunStamp, dryRun: true);
            rehearsal.Demoted.Should().Be(3);
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "an undo dry run writes nothing");

            var undone = await Service(db).UndoAsync(report.RunStamp, dryRun: false);

            undone.Demoted.Should().Be(3);
            undone.PicksRestored.Should().Be(1);
            undone.DescriptionsRemoved.Should().Be(5);
            undone.Skipped.Should().Be(1);
            undone.Lines.Should().ContainSingle(l => l.Contains("FoodGroupId changed since the run"));
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, s.Gamay).FoodGroupId.Should().Be((long)FoodGroupEnum.Dairy, "a value someone changed after the run is left alone");
            Get(db, s.Gamay).FdcCategoryId.Should().BeNull();
            Get(db, s.Burger).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, s.Burger).FoodGroupId.Should().Be((long)FoodGroupEnum.ProteinFoods);
            Get(db, s.Pepper).FoodGroupId.Should().Be((long)FoodGroupEnum.Vegetables);
            Get(db, s.Pepper).IsWholeFood.Should().BeNull();
            (await db.IngredientAliases.CountAsync()).Should().Be(0);
            var journal = await db.AuditLogEntries.ToListAsync();
            journal.Where(a => a.Timestamp == report.RunStamp).Should().NotBeEmpty("the run's journal is kept");
            journal.Where(a => a.Timestamp == undone.UndoStamp).Should().OnlyContain(a => a.ChangeType == CatalogAdmissionService.UndoChangeType)
                .And.Contain(a => a.EntityId == s.Gamay && a.PropertyName == "CurationStatusId" && a.OldValue == "9003" && a.NewValue == "9001")
                .And.Contain(a => a.EntityType == CatalogAdmissionService.RunEntityType && a.NewValue == report.RunStamp.ToString("O", CultureInfo.InvariantCulture));
            journal.Should().NotContain(a => a.Timestamp == undone.UndoStamp && a.PropertyName == "FoodGroupId" && a.EntityId == s.Gamay,
                "a value left alone was not undone");
        }

        [Fact]
        public async Task An_undo_is_journaled_and_undoing_it_puts_the_run_back()
        {
            using var db = NewContext();
            var s = Seed(db);
            var report = await Service(db).AdmitAsync(Options(dryRun: false));
            db.ChangeTracker.Clear();
            var undone = await Service(db).UndoAsync(report.RunStamp, dryRun: false);
            db.ChangeTracker.Clear();

            var redone = await Service(db).UndoAsync(undone.UndoStamp!.Value, dryRun: false);

            redone.Reapplied.Should().BeGreaterThan(0);
            redone.DescriptionsRestored.Should().Be(5);
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            Get(db, s.Gamay).FdcCategoryId.Should().Be(14);
            Get(db, s.Pepper).FoodGroupId.Should().BeNull();
            Get(db, s.Burger).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            Get(db, s.Burger).FdcCategoryId.Should().Be(21);
            (await db.IngredientAliases.CountAsync(a => a.SourceContext == IngredientLinkService.UsdaDescriptionContext)).Should().Be(5);
            (await db.AuditLogEntries.CountAsync(a => a.Timestamp == redone.UndoStamp)).Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task The_auto_classify_endpoint_and_the_enrichment_journal_each_group_they_set_or_clear_and_the_undo_takes_it_back()
        {
            using var db = NewContext();
            var pepper = new IngredientEntity { Name = "Pepper", FdcDataType = Sr, FdcCategoryId = 2, FoodGroupId = (long)FoodGroupEnum.Vegetables, CurationStatusId = 9003 };
            var gamay = new IngredientEntity { Name = "Gamay", FdcDataType = Sr, FdcCategoryId = 14, CurationStatusId = 9003 };
            db.Ingredients.AddRange(pepper, gamay);
            await db.SaveChangesAsync();

            (await new CurationOrchestrationService(db, NullLogger<CurationOrchestrationService>.Instance).AutoClassifyFoodGroupsAsync(overwrite: true, personId: 42)).Should().Be(2);

            var rows = await db.AuditLogEntries.ToListAsync();
            rows.Should().HaveCount(2).And.OnlyContain(a => a.ChangeType == CatalogAdmissionService.GroupChangeType && a.EntityType == "Ingredient"
                && a.PropertyName == "FoodGroupId" && a.ChangedByPersonId == 42 && a.Timestamp == rows[0].Timestamp);
            rows.Single(a => a.EntityId == pepper.Id).Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.OldValue == "3200" && a.NewValue == null);

            await Service(db).UndoAsync(rows[0].Timestamp, dryRun: false);
            Get(db, pepper.Id).FoodGroupId.Should().Be((long)FoodGroupEnum.Vegetables);
            Get(db, gamay.Id).FoodGroupId.Should().BeNull();

            db.ChangeTracker.Clear();
            await new FoodGroupEnrichmentService(db, NullLogger<FoodGroupEnrichmentService>.Instance).EnrichAsync(overwrite: true);

            var enrichment = await db.AuditLogEntries.Where(a => a.ChangeType == CatalogAdmissionService.GroupChangeType && a.Timestamp != rows[0].Timestamp).ToListAsync();
            enrichment.Should().Contain(a => a.EntityId == pepper.Id && a.PropertyName == "FoodGroupId" && a.OldValue == "3200" && a.NewValue == null)
                .And.Contain(a => a.EntityId == pepper.Id && a.PropertyName == "IsWholeFood" && a.NewValue == "False");
        }

        [Fact]
        public async Task A_USDA_file_not_recorded_in_the_checksum_file_stops_the_run_before_it_writes()
        {
            using var db = NewContext();
            var s = Seed(db);
            File.AppendAllText(Path.Combine(SrDir, "food_nutrient.csv"), "\"999\",\"1\",\"1008\",\"1\"\n");

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().ContainSingle(r => r.StartsWith("checksum:"));
            report.ChecksumFailures.Should().ContainSingle(f => f.Contains("food_nutrient.csv"));
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_USDA_food_without_a_category_from_the_files_stops_the_run()
        {
            using var db = NewContext();
            var s = Seed(db);
            db.Ingredients.Add(new IngredientEntity { Name = "Foundation apple of another release", FdcId = "2709215", FdcDataType = "foundation_food", CurationStatusId = (long)CurationStatusEnum.Curated });
            await db.SaveChangesAsync();

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().ContainSingle(r => r.StartsWith("category:"));
            report.NotInFile.Should().ContainSingle(l => l.Contains("fdc:2709215"));
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
        }

        [Fact]
        public async Task An_energy_pass_rate_under_99_percent_stops_the_run()
        {
            using var db = NewContext();
            var s = Seed(db);
            var baobab = Food(db, Baobab, CurationStatusEnum.PendingCuration);

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().ContainSingle(r => r.StartsWith("energy:"));
            report.EnergyByOutcome.Should().ContainKey(EnergyCheckOutcome.Mismatch).WhoseValue.Should().Be(1, "baobab powder is 21 % over by its own factors");
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, baobab.Id).EnergyCheck.Should().BeNull();
        }

        [Fact]
        public async Task A_run_that_would_change_more_foods_than_its_limit_stops()
        {
            using var db = NewContext();
            var s = Seed(db);

            var report = await Service(db).AdmitAsync(Options(dryRun: false, changeLimit: 2));

            report.Stopped.Should().ContainSingle(r => r.StartsWith("size:"));
            Get(db, s.Burger).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
        }

        [Fact]
        public async Task A_food_the_audit_would_flag_after_the_write_is_held_with_its_finding_and_the_rest_of_the_run_stands()
        {
            using var db = NewContext();
            foreach (var drink in new[] { "990001", "990002", "990003" })
                Food(db, drink, CurationStatusEnum.Curated, FoodGroupEnum.Beverages);
            var strong = Food(db, Vodka100, CurationStatusEnum.PendingCuration);
            var gamay = Food(db, Gamay, CurationStatusEnum.PendingCuration);

            var dryRun = await Service(db).AdmitAsync(Options(dryRun: true));
            dryRun.HeldForAudit.Should().ContainSingle(l => l.StartsWith($"#{strong.Id} ") && l.Contains("audit:group_energy_outlier"),
                "the dry run lays the changes over a copy and shows the hold before anything is written");

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().BeEmpty();
            report.SelfUndo.Should().BeNull("one bad food is held, not a reason to undo the run");
            report.NewFindings.Should().BeEmpty();
            report.Admitted.Should().Be(1);
            report.HeldByReason.Should().ContainKey("audit:group_energy_outlier");
            var held = Get(db, strong.Id);
            held.CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            held.FoodGroupId.Should().BeNull("295 kcal is over six times the Beverages median of 40, so it is not put in Beverages");
            held.AdmissionReason.Should().Be("audit:group_energy_outlier");
            held.FdcCategoryId.Should().Be(14, "the USDA facts are stored all the same");
            Get(db, gamay.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            Get(db, gamay.Id).FoodGroupId.Should().Be((long)FoodGroupEnum.Beverages);
            (await db.AuditLogEntries.CountAsync(a => a.EntityId == strong.Id && a.ChangeType == CatalogAdmissionService.AdmitChangeType)).Should().Be(0);

            var undo = await Service(db).UndoAsync(report.RunStamp, dryRun: false);
            undo.Demoted.Should().Be(1);
            Get(db, strong.Id).AdmissionReason.Should().BeNull("the undo takes the stored hold back with the rest of the run");
        }

        [Fact]
        public async Task A_curated_vegetable_dish_far_above_its_group_median_keeps_its_group_and_is_held_instead_of_undoing_the_run()
        {
            using var db = NewContext();
            foreach (var vegetable in new[] { "990011", "990012", "990013" })
                Food(db, vegetable, CurationStatusEnum.Curated, FoodGroupEnum.Vegetables);
            var pancakes = Food(db, PotatoPancakes, CurationStatusEnum.Curated, name: "potato pancakes");
            var gamay = Food(db, Gamay, CurationStatusEnum.PendingCuration);

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().BeEmpty();
            report.SelfUndo.Should().BeNull();
            report.HeldForAudit.Should().ContainSingle(l => l.StartsWith($"#{pancakes.Id} potato pancakes: audit:group_energy_outlier") && l.Contains("none -> Vegetables"));
            var held = Get(db, pancakes.Id);
            held.FoodGroupId.Should().BeNull("268 kcal is over six times the Vegetables median of 30; no group is better than a doubtful one");
            held.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "the pass never demotes a curated food");
            held.AdmissionReason.Should().Be("audit:group_energy_outlier");
            Get(db, gamay.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "the rest of the run is written");
        }

        [Fact]
        public async Task A_run_that_would_give_more_than_one_in_a_hundred_foods_a_new_audit_finding_stops_before_it_writes()
        {
            using var db = NewContext();
            foreach (var drink in new[] { "990001", "990002", "990003" })
                Food(db, drink, CurationStatusEnum.Curated, FoodGroupEnum.Beverages);
            var strong = Food(db, Vodka100, CurationStatusEnum.PendingCuration);
            var syrup = Food(db, "990004", CurationStatusEnum.PendingCuration);

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.Stopped.Should().ContainSingle(r => r.StartsWith("audit: 2 foods would gain a new high or medium audit finding"));
            Get(db, strong.Id).AdmissionReason.Should().BeNull();
            Get(db, syrup.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_pending_food_whose_name_a_curated_food_already_holds_is_held_as_a_duplicate()
        {
            using var db = NewContext();
            var curated = new IngredientEntity { Name = "Gamay", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(curated);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = curated.Id, AliasName = "Alcoholic Beverage, wine, table, red, Gamay", SourceContext = IngredientLinkService.UsdaDescriptionContext });
            await db.SaveChangesAsync();
            var gamay = Food(db, Gamay, CurationStatusEnum.PendingCuration);

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.HeldByReason.Should().ContainKey("duplicate");
            Get(db, gamay.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, gamay.Id).AdmissionReason.Should().Be($"duplicate_of:#{curated.Id}");
        }

        [Fact]
        public async Task The_importer_judges_each_new_food_by_the_rule_and_groups_it_by_its_category_only_but_curates_none()
        {
            using var db = NewContext();

            var report = await new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance).ImportAsync(SrDir, dataType: Sr);

            report.Accepted.Should().Be(14);
            report.Admissible.Should().Be(14, "the wines, vodkas, pepper, the test drinks, the test vegetables, potato pancakes and the two prepared foods pass; baobab fails its own factors");
            var foods = await db.Ingredients.ToDictionaryAsync(i => i.FdcId!);
            foods.Values.Should().OnlyContain(f => f.CurationStatusId == (long)CurationStatusEnum.PendingCuration,
                "only --admit curates, behind its checksum, energy-rate, size and audit gates");
            foods[Pepper].FoodGroupId.Should().BeNull("Spices and Herbs gives no group; the name \"pepper\" never makes it a vegetable");
            foods[Pepper].FdcCategoryId.Should().Be(2);
            foods[Pepper].EnergyCheck.Should().Be(EnergyCheckOutcome.ExemptSmall);
            foods[Gamay].EnergyCheck.Should().Be(EnergyCheckOutcome.OwnFactors);
            foods[Gamay].AdmissionReason.Should().BeNull("the rule finds nothing against it");
            foods[Burger].AdmissionReason.Should().BeNull("a prepared food passes the rule and waits for --admit like any other");
            foods[Burger].FoodGroupId.Should().BeNull();
            PreparedFoods.IsPrepared(foods[Burger]).Should().BeTrue();
            foods[Applesauce].AdmissionReason.Should().BeNull();
            PreparedFoods.IsPrepared(foods[Applesauce]).Should().BeTrue();
            foods.Should().NotContainKey(Baobab, "a food that fails the energy check is not imported");
            report.RejectedByReason.Should().ContainKey(EnergyCheckOutcome.Mismatch);
        }

        [Fact]
        public async Task A_branded_food_is_grouped_by_its_category_alone_stores_it_and_waits_for_a_second_source()
        {
            var dir = Path.Combine(_root, "branded");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "branded_food.csv"),
                "\"fdc_id\",\"brand_owner\",\"gtin_upc\",\"serving_size\",\"serving_size_unit\",\"branded_food_category\",\"market_country\",\"discontinued_date\"\n"
                + "\"2000001\",\"Acme\",\"000111\",\"14\",\"g\",\"Vegetable & Cooking Oils\",\"United States\",\"\"\n"
                + "\"2000002\",\"Acme\",\"000222\",\"28\",\"g\",\"Snack, Energy & Granola Bars\",\"United States\",\"\"\n");
            File.WriteAllText(Path.Combine(dir, "food.csv"),
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"\n"
                + "\"2000001\",\"branded_food\",\"Canola and olive oil blend\",\"\",\"2025-12-18\"\n"
                + "\"2000002\",\"branded_food\",\"Ramen noodle soup chips\",\"\",\"2025-12-18\"\n");
            File.WriteAllText(Path.Combine(dir, "food_nutrient.csv"),
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n"
                + "\"1\",\"2000001\",\"1008\",\"884\"\n\"2\",\"2000001\",\"1003\",\"0\"\n\"3\",\"2000001\",\"1005\",\"0\"\n\"4\",\"2000001\",\"1004\",\"100\"\n"
                + "\"5\",\"2000002\",\"1008\",\"400\"\n\"6\",\"2000002\",\"1003\",\"10\"\n\"7\",\"2000002\",\"1005\",\"60\"\n\"8\",\"2000002\",\"1004\",\"13\"\n");
            using var db = NewContext();

            var report = await new FdcBrandedImportService(db, NullLogger<FdcBrandedImportService>.Instance).ImportAsync(dir, 10);

            report.Accepted.Should().Be(2);
            var foods = await db.Ingredients.ToDictionaryAsync(i => i.FdcId!);
            foods["2000001"].FoodGroupId.Should().Be((long)FoodGroupEnum.FatsOils, "a cooking-oil category is not a serving of vegetables");
            foods["2000001"].FdcCategory.Should().Be("Vegetable & Cooking Oils");
            foods["2000001"].IsWholeFood.Should().BeFalse();
            foods["2000002"].FoodGroupId.Should().Be((long)FoodGroupEnum.SweetsSnacks);
            foods.Values.Should().OnlyContain(f => f.CurationStatusId == (long)CurationStatusEnum.PendingCuration
                && f.AdmissionReason == CatalogAdmissionRule.AwaitingSecondSource && f.EnergyCheck == EnergyCheckOutcome.General);
        }

        [Fact]
        public async Task The_category_only_enrichment_never_groups_a_food_by_its_name()
        {
            using var db = NewContext();
            db.Ingredients.AddRange(
                new IngredientEntity { Name = "Pepper", FdcDataType = Sr, FdcCategoryId = 2, CurationStatusId = 9003 },
                new IngredientEntity { Name = "Steak Sauce", CurationStatusId = 9003 },
                new IngredientEntity { Name = "Gamay", FdcDataType = Sr, FdcCategoryId = 14, CurationStatusId = 9003 },
                new IngredientEntity { Name = "Canola blend (Acme)", FdcDataType = "branded_food", FdcCategory = "Vegetable & Cooking Oils", CurationStatusId = 9001 });
            await db.SaveChangesAsync();

            await new FoodGroupEnrichmentService(db, NullLogger<FoodGroupEnrichmentService>.Instance).EnrichAsync(overwrite: false);

            var groups = await db.Ingredients.ToDictionaryAsync(i => i.Name, i => i.FoodGroupId);
            groups["Pepper"].Should().BeNull();
            groups["Steak Sauce"].Should().BeNull("\"tea\" inside \"steak\" once made it a beverage");
            groups["Gamay"].Should().Be((long)FoodGroupEnum.Beverages);
            groups["Canola blend (Acme)"].Should().Be((long)FoodGroupEnum.FatsOils, "a cooking-oil category is not Vegetables");
        }

        private sealed class SystemUser : Nom.Orch.Interfaces.ICurrentUserService
        {
            public long? PersonId => SystemConstants.SystemPersonId;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => SystemConstants.SystemPersonId;
            public string? UserId => "system";
            public string RequiredUserId => "system";
        }

        private static LineRelinkService Relink(ApplicationDbContext db) => new(db, new SystemUser());

        private static async Task<long> RelinkWithdrawsAsync(ApplicationDbContext db, DateTime at)
        {
            var retired = new IngredientEntity { Name = "minced collops", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.Add(retired);
            await db.SaveChangesAsync();
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = retired.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = retired.Name,
                Source = "deterministic:name-match", Status = FoodProposalStatus.Pending, IsDeleted = true, DeletedAt = at, DeletedByPersonId = SystemConstants.SystemPersonId,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = LineRelinkService.PickWithdrawEntityType, EntityId = proposal.Id, ChangeType = LineRelinkService.ProposalWithdrawChangeType,
                PropertyName = nameof(FoodCatalogProposalEntity.IsDeleted), OldValue = bool.FalseString, NewValue = bool.TrueString,
                Timestamp = at, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return proposal.Id;
        }

        private static bool Withdrawn(ApplicationDbContext db, long proposalId) =>
            db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking().Single(p => p.Id == proposalId).IsDeleted;

        [Fact]
        public async Task The_relink_undo_after_an_admission_restores_only_its_own_withdrawal_and_the_admission_undo_only_its_own()
        {
            using var db = NewContext();
            var s = Seed(db);
            var before = DateTime.UtcNow.AddSeconds(-1);
            var report = await Service(db).AdmitAsync(Options(dryRun: false));
            db.ChangeTracker.Clear();
            var relinked = await RelinkWithdrawsAsync(db, report.RunStamp.AddSeconds(1));

            var relinkUndo = await Relink(db).UndoAsync(before, apply: true);

            relinkUndo.ProposalsRestored.Should().Be(1);
            relinkUndo.Skipped.Should().Be(0);
            Withdrawn(db, relinked).Should().BeFalse();
            Withdrawn(db, s.Pick).Should().BeTrue("the admission's withdrawal is the admission undo's to take back (K15)");

            db.ChangeTracker.Clear();
            var admissionUndo = await Service(db).UndoAsync(report.RunStamp, dryRun: false);

            admissionUndo.PicksRestored.Should().Be(1);
            Withdrawn(db, s.Pick).Should().BeFalse();
            Withdrawn(db, relinked).Should().BeFalse();
        }

        [Fact]
        public async Task The_admission_undo_after_a_relink_restores_only_its_own_withdrawal_and_the_relink_undo_only_its_own()
        {
            using var db = NewContext();
            var s = Seed(db);
            var relinkedAt = DateTime.UtcNow.AddSeconds(-5);
            var relinked = await RelinkWithdrawsAsync(db, relinkedAt);
            var report = await Service(db).AdmitAsync(Options(dryRun: false));
            db.ChangeTracker.Clear();

            var admissionUndo = await Service(db).UndoAsync(report.RunStamp, dryRun: false);

            admissionUndo.PicksRestored.Should().Be(1);
            admissionUndo.Skipped.Should().Be(0);
            Withdrawn(db, s.Pick).Should().BeFalse();
            Withdrawn(db, relinked).Should().BeTrue("the relink's withdrawal is the relink undo's to take back");

            db.ChangeTracker.Clear();
            var pickedAgain = db.FoodCatalogProposals.Single(p => p.Id == s.Pick);
            pickedAgain.IsDeleted = true;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var relinkUndo = await Relink(db).UndoAsync(relinkedAt, apply: true);

            relinkUndo.ProposalsRestored.Should().Be(1);
            relinkUndo.Skipped.Should().Be(0, "neither the admission's withdrawal nor the entries of its undo are the relink's");
            Withdrawn(db, relinked).Should().BeFalse();
            Withdrawn(db, s.Pick).Should().BeTrue("a pick withdrawn since by another job stays withdrawn");
        }

        [Fact]
        public async Task A_withdrawal_an_admission_journaled_before_it_had_its_own_type_is_left_out_of_the_relink_undo()
        {
            using var db = NewContext();
            var s = Seed(db);
            var stamp = DateTime.UtcNow.AddMinutes(-10);
            db.AuditLogEntries.AddRange(
                new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = CatalogAdmissionService.RunEntityType, EntityId = 0, ChangeType = CatalogAdmissionService.RunChangeType,
                    PropertyName = "RuleVersion", NewValue = "catalog-admission/2", Timestamp = stamp, ChangedByPersonId = SystemConstants.SystemPersonId,
                },
                new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.PickWithdrawEntityType, EntityId = s.Pick, ChangeType = LineRelinkService.ProposalWithdrawChangeType,
                    PropertyName = nameof(FoodCatalogProposalEntity.IsDeleted), OldValue = bool.FalseString, NewValue = bool.TrueString,
                    Timestamp = stamp, ChangedByPersonId = SystemConstants.SystemPersonId,
                });
            db.FoodCatalogProposals.Single(p => p.Id == s.Pick).IsDeleted = true;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var relinkUndo = await Relink(db).UndoAsync(stamp.AddMinutes(-1), apply: true);

            relinkUndo.ProposalsRestored.Should().Be(0);
            relinkUndo.Skipped.Should().Be(0);
            Withdrawn(db, s.Pick).Should().BeTrue("checkpoint 1's 602 admission withdrawals carry the relink's type and the admission run's stamp");
        }
    }
}
