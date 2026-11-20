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
    /// importers that apply the same rule as they create foods: SR Legacy facts from the 2018-04
    /// release, admitted, rejected by category, regrouped from the category alone, journaled,
    /// undone, and stopped by its own gates before it writes.
    /// </summary>
    public class CatalogAdmissionTests : IDisposable
    {
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, Alcohol = 5038;
        private const string Sr = "sr_legacy_food";
        private const string Gamay = "171872", Vodka = "174818", Vodka100 = "173664", Burger = "170717", Pepper = "170931", Applesauce = "173512", Baobab = "167806";

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
                "\"id\",\"code\",\"description\"\n\"2\",\"0200\",\"Spices and Herbs\"\n\"3\",\"0300\",\"Baby Foods\"\n\"9\",\"0900\",\"Fruits and Fruit Juices\"\n"
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
            report.Admitted.Should().Be(2, "Gamay and vodka pass every check");
            report.Rejected.Should().Be(1);
            report.RejectedByReason.Should().Equal(new Dictionary<string, int> { ["category:Fast Foods"] = 1 });
            report.KeptCurated.Should().Be(1, "curated applesauce sits in Baby Foods but the pass never demotes a curated food");
            report.GroupMoves.Should().ContainKey("Vegetables -> none").WhoseValue.Should().Be(1);
            report.PicksWithdrawn.Should().Be(1);
            report.DescriptionsAdded.Should().Be(5);
            report.EnergyPassRate.Should().Be(1m);
            report.CategoryMissing.Should().Be(0);
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, s.Pepper).FoodGroupId.Should().Be((long)FoodGroupEnum.Vegetables);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
            (await db.IngredientAliases.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task An_apply_admits_rejects_regroups_and_journals_every_change_under_one_stamp()
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
            burger.CurationStatusId.Should().Be((long)CurationStatusEnum.Rejected);
            burger.AdmissionReason.Should().Be("category:Fast Foods");
            burger.FoodGroupId.Should().BeNull("Fast Foods gives no group");
            var pepper = Get(db, s.Pepper);
            pepper.FoodGroupId.Should().BeNull("black pepper leaves Vegetables");
            pepper.IsWholeFood.Should().BeFalse();
            pepper.EnergyCheck.Should().Be(EnergyCheckOutcome.ExemptSmall);
            Get(db, s.Applesauce).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            Get(db, s.Applesauce).AdmissionReason.Should().Be("category:Baby Foods");

            (await db.IngredientAliases.Where(a => a.IngredientId == s.Pepper).Select(a => a.AliasName + "|" + a.SourceContext).SingleAsync())
                .Should().Be("Spices, pepper, black|" + IngredientLinkService.UsdaDescriptionContext);
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == s.Pick)).IsDeleted.Should().BeTrue();

            var journal = await db.AuditLogEntries.ToListAsync();
            journal.Should().OnlyContain(a => a.Timestamp == report.RunStamp);
            journal.Single(a => a.ChangeType == CatalogAdmissionService.RunChangeType).NewValue.Should().Be(CatalogAdmissionRule.Version);
            journal.Single(a => a.EntityId == s.Gamay && a.ChangeType == CatalogAdmissionService.AdmitChangeType)
                .Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.PropertyName == "CurationStatusId" && a.OldValue == "9001" && a.NewValue == "9003");
            journal.Single(a => a.EntityId == s.Burger && a.ChangeType == CatalogAdmissionService.RejectChangeType).NewValue.Should().Be("9004");
            journal.Single(a => a.EntityId == s.Pepper && a.ChangeType == CatalogAdmissionService.GroupChangeType && a.PropertyName == "FoodGroupId")
                .Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.OldValue == "3200" && a.NewValue == null);
            journal.Should().Contain(a => a.EntityId == s.Pick && a.ChangeType == LineRelinkService.ProposalWithdrawChangeType);
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
            rehearsal.Demoted.Should().Be(2);
            Get(db, s.Gamay).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "an undo dry run writes nothing");

            var undone = await Service(db).UndoAsync(report.RunStamp, dryRun: false);

            undone.Demoted.Should().Be(2);
            undone.Unrejected.Should().Be(1);
            undone.DescriptionsRemoved.Should().Be(5);
            undone.PicksRestored.Should().Be(1);
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
            (await db.FoodCatalogProposals.SingleAsync(p => p.Id == s.Pick)).IsDeleted.Should().BeFalse();
            (await db.AuditLogEntries.Select(a => a.PropertyName).ToListAsync()).Should().Equal("FoodGroupId");
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
        public async Task A_run_undoes_itself_when_the_audit_finds_something_new_on_a_food_it_regrouped()
        {
            using var db = NewContext();
            foreach (var drink in new[] { "990001", "990002", "990003" })
                Food(db, drink, CurationStatusEnum.Curated, FoodGroupEnum.Beverages);
            var strong = Food(db, Vodka100, CurationStatusEnum.PendingCuration);

            var report = await Service(db).AdmitAsync(Options(dryRun: false));

            report.NewFindings.Should().ContainSingle(f => f == $"#{strong.Id}: group_energy_outlier", "295 kcal is over six times the Beverages median of 40");
            report.SelfUndo.Should().NotBeNull();
            Get(db, strong.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            Get(db, strong.Id).FoodGroupId.Should().BeNull();
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
        public async Task The_importer_judges_each_new_food_by_the_rule_and_groups_it_by_its_category_only()
        {
            using var db = NewContext();

            var report = await new FdcFoundationImportService(db, NullLogger<FdcFoundationImportService>.Instance).ImportAsync(SrDir, dataType: Sr);

            report.Accepted.Should().Be(9);
            report.Curated.Should().Be(7, "the wines, vodkas, pepper and the test drinks pass; baobab fails its own factors");
            report.RejectedByRule.Should().Be(2);
            var foods = await db.Ingredients.ToDictionaryAsync(i => i.FdcId!);
            foods[Pepper].FoodGroupId.Should().BeNull("Spices and Herbs gives no group; the name \"pepper\" never makes it a vegetable");
            foods[Pepper].FdcCategoryId.Should().Be(2);
            foods[Pepper].EnergyCheck.Should().Be(EnergyCheckOutcome.ExemptSmall);
            foods[Gamay].EnergyCheck.Should().Be(EnergyCheckOutcome.OwnFactors);
            foods[Gamay].CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            foods[Burger].CurationStatusId.Should().Be((long)CurationStatusEnum.Rejected);
            foods[Burger].FoodGroupId.Should().BeNull();
            foods[Applesauce].AdmissionReason.Should().Be("category:Baby Foods");
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
    }
}
