using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Loading the reviewed wording table: what a load refuses, what it journals, and how a batch
    /// comes out again. Wordings and line counts in the fixture are staging's top unresolved ones.
    /// </summary>
    public class VerifiedWordingTests
    {
        private const string Batch = "w01";

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static VerifiedWordingService Service(ApplicationDbContext db) => new(db, new NoUser());

        private static IngredientEntity Food(long id, string name, string? fdcId = null, string dataType = "sr_legacy_food",
            CurationStatusEnum status = CurationStatusEnum.Curated, bool nutrition = true) => new()
        {
            Id = id,
            Name = name,
            FdcId = fdcId,
            FdcDataType = fdcId == null ? string.Empty : dataType,
            CurationStatusId = (long)status,
            IngredientNutrients = nutrition ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
        };

        private static async Task SeedAsync(ApplicationDbContext db)
        {
            db.Ingredients.AddRange(Food(900, "Vanilla Extract"), Food(901, "Fresh Thyme", "173470"), Food(902, "Dried Thyme", "170938"));
            await db.SaveChangesAsync();
        }

        private static List<VerifiedWordingRow> Fixture(List<VerifiedWordingProblem> problems) =>
            VerifiedWordingService.ParseTsv(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "VerifiedWordings", "batch-w01.tsv")), problems);

        private static VerifiedWordingRow Row(string wording, string verdict, long? foodId = null, string? fdcId = null, string? name = null) =>
            new() { Row = wording, Wording = wording, Verdict = verdict, FoodId = foodId, FoodFdcId = fdcId, FoodName = name };

        [Fact]
        public void The_tsv_is_read_by_its_header_and_a_short_line_is_reported()
        {
            var problems = new List<VerifiedWordingProblem>();

            var rows = Fixture(problems);

            rows.Select(r => r.Wording).Should().Equal("thyme", "vanilla", "white wine", "kosher salt and pepper", "lemon rind", "Crisco");
            rows[0].Should().BeEquivalentTo(new VerifiedWordingRow
            {
                Row = "W01-6", Wording = "thyme", Verdict = "food", FoodId = null, FoodFdcId = "173470", FoodName = "Fresh Thyme",
                Note = "sprigs and bunches are fresh thyme",
            });
            rows[1].FoodId.Should().Be(900);
            problems.Should().ContainSingle().Which.Should().Match<VerifiedWordingProblem>(p =>
                p.Wording == "sweet herbs" && p.Reason == VerifiedWordingService.MalformedLine);
        }

        [Fact]
        public void The_header_the_repository_file_starts_with_is_the_one_the_loader_reads()
        {
            var problems = new List<VerifiedWordingProblem>();

            VerifiedWordingService.ParseTsv(VerifiedWordingService.TsvHeader + "\n", problems).Should().BeEmpty();
            problems.Should().BeEmpty();
        }

        [Fact]
        public async Task A_dry_run_reports_what_the_load_would_do_and_writes_nothing()
        {
            using var db = NewContext();
            await SeedAsync(db);

            var result = await Service(db).LoadAsync(Fixture(new List<VerifiedWordingProblem>()), Batch);

            result.Should().Match<VerifiedWordingLoadResult>(r => !r.Applied && r.Rows == 6 && r.Added == 4 && r.Rejected == 2 && r.Promoted == 0);
            result.Problems.Select(p => (p.Wording, p.Reason)).Should().BeEquivalentTo(new[]
            {
                ("lemon rind", VerifiedWordingService.TargetMissing),
                ("Crisco", VerifiedWordingService.NotKeyForm),
            });
            result.BusiestFoods.Select(f => f.Name).Should().BeEquivalentTo("Vanilla Extract", "Fresh Thyme");
            (await db.IngredientWordings.AnyAsync()).Should().BeFalse();
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task An_applied_load_adds_each_row_with_one_journal_entry()
        {
            using var db = NewContext();
            await SeedAsync(db);

            var result = await Service(db).LoadAsync(Fixture(new List<VerifiedWordingProblem>()), Batch, apply: true);

            result.Added.Should().Be(4);
            var rows = await db.IngredientWordings.ToDictionaryAsync(w => w.Wording);
            rows.Keys.Should().BeEquivalentTo("thyme", "vanilla", "white wine", "kosher salt and pepper");
            rows["thyme"].Should().Match<IngredientWordingEntity>(w => w.Verdict == "food" && w.IngredientId == 901
                && w.Batch == Batch && w.Source == "verified:w01" && w.Note == "sprigs and bunches are fresh thyme");
            rows["vanilla"].IngredientId.Should().Be(900);
            rows["white wine"].IngredientId.Should().BeNull();
            rows["kosher salt and pepper"].Should().Match<IngredientWordingEntity>(w => w.Verdict == "unclear" && w.IngredientId == null);
            var journal = await db.AuditLogEntries.ToListAsync();
            journal.Should().HaveCount(4).And.OnlyContain(a => a.EntityType == VerifiedWordingService.WordingEntityType
                && a.ChangeType == VerifiedWordingService.VerifyChangeType && a.OldValue == null);
            journal.Single(a => a.EntityId == rows["thyme"].Id).NewValue.Should().Be("food:901");
            journal.Single(a => a.EntityId == rows["white wine"].Id).NewValue.Should().Be("none:");
        }

        [Fact]
        public async Task A_food_the_catalog_rejected_is_never_promoted_to_carry_a_wording()
        {
            using var db = NewContext();
            await SeedAsync(db);
            db.Ingredients.Add(Food(920, "McDONALD'S, Hamburger", "170717", status: CurationStatusEnum.Rejected));
            await db.SaveChangesAsync();

            var result = await Service(db).LoadAsync(new List<VerifiedWordingRow> { Row("butcher's meat", "food", fdcId: "170717") }, Batch, apply: true);

            result.Problems.Select(p => (p.Wording, p.Reason)).Should().Equal(("butcher's meat", VerifiedWordingService.TargetNotCurated));
            (await db.Ingredients.SingleAsync(i => i.Id == 920)).CurationStatusId.Should().Be((long)CurationStatusEnum.Rejected);
        }

        [Fact]
        public async Task A_food_the_admission_rule_holds_is_never_promoted_to_carry_a_wording()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var baobab = Food(921, "Baobab powder", "167806", status: CurationStatusEnum.PendingCuration);
            baobab.AdmissionReason = EnergyCheckOutcome.Mismatch;
            db.Ingredients.Add(baobab);
            await db.SaveChangesAsync();

            var result = await Service(db).LoadAsync(new List<VerifiedWordingRow> { Row("baobab", "food", fdcId: "167806") }, Batch, apply: true);

            result.Problems.Select(p => (p.Wording, p.Reason)).Should().Equal(("baobab", VerifiedWordingService.TargetNotCurated));
            (await db.Ingredients.SingleAsync(i => i.Id == 921)).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
        }

        [Fact]
        public async Task A_prepared_food_never_carries_a_wording_curated_or_not()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var burger = Food(922, "McDONALD'S, Hamburger", "170717");
            burger.FdcCategoryId = 21;
            var nachos = Food(923, "Fast foods, nachos, with cheese", "170291", status: CurationStatusEnum.PendingCuration);
            nachos.FdcCategoryId = 21;
            db.Ingredients.AddRange(burger, nachos);
            await db.SaveChangesAsync();

            var result = await Service(db).LoadAsync(new List<VerifiedWordingRow>
            {
                Row("hamburger", "food", fdcId: "170717"),
                Row("nachos", "food", fdcId: "170291"),
            }, Batch, apply: true);

            result.Problems.Select(p => (p.Wording, p.Reason)).Should().Equal(
                ("hamburger", VerifiedWordingService.TargetPrepared),
                ("nachos", VerifiedWordingService.TargetPrepared));
            (await db.IngredientWordings.AnyAsync(w => w.IngredientId == 922 || w.IngredientId == 923)).Should().BeFalse();
            (await db.Ingredients.SingleAsync(i => i.Id == 923)).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration, "a wording load never promotes a prepared food");
        }

        [Fact]
        public async Task A_row_is_refused_when_its_food_cannot_carry_the_wording()
        {
            using var db = NewContext();
            await SeedAsync(db);
            db.Ingredients.AddRange(
                Food(910, "Retired Cream", "170859"),
                Food(911, "SILK Very Vanilla, soymilk", "1097542", dataType: "branded_food", status: CurationStatusEnum.PendingCuration),
                Food(912, "Kosher Salt", status: CurationStatusEnum.NonCurated),
                Food(913, "Dry White Wine", nutrition: false),
                Food(914, "Chicken Broth"));
            await db.SaveChangesAsync();
            var retired = await db.Ingredients.SingleAsync(i => i.Id == 910);
            retired.IsDeleted = true;
            await db.SaveChangesAsync();

            var result = await Service(db).LoadAsync(new List<VerifiedWordingRow>
            {
                Row("cream", "food", fdcId: "170859"),
                Row("very vanilla", "food", fdcId: "1097542"),
                Row("kosher salt", "food", foodId: 912, name: "Kosher Salt"),
                Row("white wine", "food", foodId: 913, name: "Dry White Wine"),
                Row("broth", "food", foodId: 914, name: "Beef Broth"),
                Row("chicken broth", "none"),
                Row("dried thyme", "food", fdcId: "173470"),
                Row("sweet milk", "maybe"),
                Row("2 cups vanilla", "food", foodId: 900, name: "Vanilla Extract"),
                Row("thyme", "food", fdcId: "173470"),
                Row("thyme", "food", fdcId: "170938"),
                Row("vanilla", "food", foodId: 900, name: "Vanilla Extract"),
                Row("vanilla", "food", foodId: 900, name: "vanilla extract"),
            }, Batch, apply: true);

            result.Problems.Select(p => (p.Wording, p.Reason)).Should().BeEquivalentTo(new[]
            {
                ("cream", VerifiedWordingService.TargetMissing),
                ("very vanilla", VerifiedWordingService.TargetBranded),
                ("kosher salt", VerifiedWordingService.TargetNotCurated),
                ("white wine", VerifiedWordingService.TargetNoNutrition),
                ("broth", VerifiedWordingService.TargetMissing),
                ("chicken broth", VerifiedWordingService.NamesCuratedFood),
                ("dried thyme", VerifiedWordingService.NamesCuratedFood),
                ("sweet milk", VerifiedWordingService.UnknownVerdict),
                ("2 cups vanilla", VerifiedWordingService.NotKeyForm),
                ("thyme", VerifiedWordingService.ConflictingRows),
                ("thyme", VerifiedWordingService.ConflictingRows),
                ("vanilla", VerifiedWordingService.DuplicateRow),
            });
            result.Added.Should().Be(1);
            (await db.IngredientWordings.Select(w => w.Wording).ToListAsync()).Should().Equal("vanilla");
        }

        [Fact]
        public async Task A_reload_is_unchanged_and_another_food_needs_replace_and_is_journaled_with_the_old_one()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var service = Service(db);
            await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "170938") }, Batch, apply: true);

            var again = await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "170938") }, "w02", apply: true);
            var refused = await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "173470") }, "w02", apply: true);
            var replaced = await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "173470") }, "w02", apply: true, replace: true);

            again.Unchanged.Should().Be(1);
            refused.Problems.Should().ContainSingle().Which.Reason.Should().Be(VerifiedWordingService.ExistsDifferent);
            replaced.Changed.Should().Be(1);
            var row = await db.IngredientWordings.SingleAsync();
            row.Should().Match<IngredientWordingEntity>(w => w.IngredientId == 901 && w.Batch == "w02");
            (await db.AuditLogEntries.Where(a => a.PropertyName == nameof(IngredientWordingEntity.IngredientId)).OrderBy(a => a.Id).Select(a => new { a.OldValue, a.NewValue }).ToListAsync()).Should().Equal(
                new { OldValue = (string?)null, NewValue = (string?)"food:902" },
                new { OldValue = (string?)"food:902", NewValue = (string?)"food:901" });
        }

        [Fact]
        public async Task Unloading_a_batch_that_replaced_a_verdict_puts_the_earlier_batchs_verdict_back()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var service = Service(db);
            await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "170938") }, Batch, apply: true);
            await service.LoadAsync(new[] { Row("thyme", "unclear") }, "w02", apply: true, replace: true);

            var dryRun = await service.UnloadAsync("w02");
            var done = await service.UnloadAsync("w02", apply: true);

            dryRun.Should().BeEquivalentTo(new { Applied = false, Removed = 0, Restored = 1 });
            done.Restored.Should().Be(1);
            var row = await db.IngredientWordings.SingleAsync();
            row.Should().Match<IngredientWordingEntity>(w => w.Verdict == "food" && w.IngredientId == 902 && w.Batch == Batch && !w.IsDeleted);
            (await service.UnloadAsync(Batch, apply: true)).Removed.Should().Be(1, "the earlier batch finds its row again");
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task An_unload_takes_the_batch_out_and_a_later_load_brings_the_row_back()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var service = Service(db);
            await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "173470"), Row("white wine", "none") }, Batch, apply: true);
            await service.LoadAsync(new[] { Row("vanilla", "food", foodId: 900, name: "Vanilla Extract") }, "w02", apply: true);
            var id = (await db.IngredientWordings.SingleAsync(w => w.Wording == "thyme")).Id;

            var dryRun = await service.UnloadAsync(Batch);
            (await db.IngredientWordings.CountAsync()).Should().Be(3);
            var unloaded = await service.UnloadAsync(Batch, apply: true);

            dryRun.Removed.Should().Be(2);
            unloaded.Should().Match<VerifiedWordingUnloadResult>(r => r.Applied && r.Removed == 2 && r.Demoted == 0);
            (await db.IngredientWordings.Select(w => w.Wording).ToListAsync()).Should().Equal("vanilla");
            (await db.AuditLogEntries.CountAsync(a => a.NewValue == null && a.ChangeType == VerifiedWordingService.VerifyChangeType)).Should().Be(2);

            var reloaded = await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "173470") }, "w03", apply: true);

            reloaded.Added.Should().Be(1);
            (await db.IngredientWordings.SingleAsync(w => w.Wording == "thyme")).Should().Match<IngredientWordingEntity>(w => w.Id == id && w.Batch == "w03");
        }

        [Fact]
        public async Task A_usda_food_awaiting_curation_is_curated_by_the_load_and_goes_back_once_no_wording_needs_it()
        {
            using var db = NewContext();
            await SeedAsync(db);
            db.Ingredients.Add(Food(920, "Lemon peel, raw", "167753", status: CurationStatusEnum.PendingCuration));
            await db.SaveChangesAsync();
            var service = Service(db);

            var dryRun = await service.LoadAsync(new[] { Row("lemon rind", "food", fdcId: "167753") }, Batch);
            (await db.Ingredients.SingleAsync(i => i.Id == 920)).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            var loaded = await service.LoadAsync(new[] { Row("lemon rind", "food", fdcId: "167753") }, Batch, apply: true);
            await service.LoadAsync(new[] { Row("lemon zest", "food", fdcId: "167753") }, "w02", apply: true);

            dryRun.Promoted.Should().Be(1);
            loaded.Promoted.Should().Be(1);
            (await db.Ingredients.SingleAsync(i => i.Id == 920)).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == VerifiedWordingService.CurateChangeType))
                .Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.EntityId == 920 && a.OldValue == "9001" && a.NewValue == "9003");

            (await service.UnloadAsync(Batch, apply: true)).Demoted.Should().Be(0, "lemon zest still points at it");
            (await service.UnloadAsync("w02", apply: true)).Demoted.Should().Be(1);

            (await db.Ingredients.SingleAsync(i => i.Id == 920)).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            (await db.AuditLogEntries.AnyAsync(a => a.ChangeType == VerifiedWordingService.CurateChangeType)).Should().BeFalse();
        }

        [Fact]
        public async Task The_export_lists_the_live_table_with_each_food()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var service = Service(db);
            await service.LoadAsync(new[] { Row("thyme", "food", fdcId: "173470"), Row("white wine", "none") }, Batch, apply: true);

            var tsv = await service.ExportTsvAsync();

            tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(
                VerifiedWordingService.ExportHeader,
                "thyme\tfood\t901\t173470\tFresh Thyme\tw01\t",
                "white wine\tnone\t\t\t\tw01\t");
        }
    }
}
