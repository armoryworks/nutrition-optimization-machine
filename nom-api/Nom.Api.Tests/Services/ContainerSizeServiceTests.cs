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
using Nom.Data.Recipe;
using Nom.Import.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The container size lane against a database: an import writes sizes in shadow with a journal,
    /// the hold-out of stated sizes sets each key's status and runs the lane on the frame, the undo
    /// takes a run back with nothing skipped, and the import stops on a file its checksums do not list.
    /// </summary>
    public class ContainerSizeServiceTests
    {
        private const long Ounce = 6, Can = 7;
        private const long Operator = 2;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Rig
        {
            public required ApplicationDbContext Db { get; init; }
            public required ContainerSizeService Sizes { get; init; }
            public required LaneFrameService Frame { get; init; }
            public long Beans { get; set; }
        }

        private static async Task<Rig> RigAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Ounce, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1, BaseUnitConversionFactor = 28.3495m },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, IsContainer = true });
            var beans = new IngredientEntity { Name = "Black Beans", FdcId = "175238", FdcDataType = "sr_legacy_food", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(beans);
            await db.SaveChangesAsync();
            var undo = new List<ILaneUndo>();
            var frame = new LaneFrameService(db, undo, NullLogger<LaneFrameService>.Instance);
            var sizes = new ContainerSizeService(db, frame, NullLogger<ContainerSizeService>.Instance);
            undo.Add(new ContainerSizeLaneUndo(sizes));
            return new Rig { Db = db, Sizes = sizes, Frame = frame, Beans = beans.Id };
        }

        private static ContainerSizeProposal Proposal(long food, decimal grams, string source = "deterministic:fdc-branded-mode:2025-12-18:canned-black-beans:223/304") =>
            new(food, ContainerKinds.Can, ContainerEras.Modern, string.Empty, grams, null, $"{grams} g can", source, "canned-black-beans", 1, 0.73m, 304);

        private static async Task LinesAsync(Rig rig, int count, decimal ounces, string site = "example.org")
        {
            var recipe = new RecipeEntity { Name = "Bean dish", SourceSite = site, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            for (var i = 0; i < count; i++)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    IngredientId = rig.Beans, SortOrder = i, Quantity = ounces, MeasurementId = Ounce,
                    RawLine = $"1 ({ounces} oz) can black beans, rinsed", CreatedDate = Imported.AddMinutes(i),
                });
            rig.Db.Recipes.Add(recipe);
            await rig.Db.SaveChangesAsync();
        }

        [Fact]
        public async Task An_import_writes_new_sizes_in_shadow_journals_each_and_a_second_run_changes_nothing()
        {
            var rig = await RigAsync();
            var stamp = DateTime.UtcNow;
            var dry = await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, dryRun: true, stamp, retireMissing: true, Operator);
            dry.Added.Should().Be(1);
            (await rig.Db.IngredientContainerSizes.CountAsync()).Should().Be(0, "a dry run writes nothing");

            var first = await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, dryRun: false, stamp, retireMissing: true, Operator);
            first.Added.Should().Be(1);
            var row = await rig.Db.IngredientContainerSizes.SingleAsync();
            row.Status.Should().Be(ContainerSizeStatus.Shadow);
            row.NetGrams.Should().Be(425m);
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == ContainerSizeService.SetChange && a.EntityId == row.Id)).Should().Be(1);

            var again = await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, dryRun: false, stamp.AddMinutes(1), retireMissing: true, Operator);
            again.Unchanged.Should().Be(1);
            again.Added.Should().Be(0);
        }

        [Fact]
        public async Task A_changed_size_replaces_the_old_row_and_a_size_no_source_gives_is_retired()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            var replaced = await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 439m) }, false, DateTime.UtcNow, true, Operator);
            replaced.Replaced.Should().Be(1);
            (await rig.Db.IngredientContainerSizes.Where(r => r.Status != ContainerSizeStatus.Retired).Select(r => r.NetGrams).SingleAsync()).Should().Be(439m);

            var gone = await rig.Sizes.WriteAsync(Array.Empty<ContainerSizeProposal>(), false, DateTime.UtcNow, true, Operator);
            gone.Retired.Should().Be(1);
            (await rig.Db.IngredientContainerSizes.CountAsync(r => r.Status != ContainerSizeStatus.Retired)).Should().Be(0);
        }

        [Fact]
        public async Task The_hold_out_makes_a_key_that_predicts_stated_sizes_active_and_records_every_comparison_on_the_frame()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(rig, 38, 15m);
            await LinesAsync(rig, 2, 15.5m);

            var dry = await rig.Sizes.ScoreAsync(apply: false, Operator);
            dry.HoldoutLines.Should().Be(40);
            dry.HoldoutAgree.Should().Be(40, "15.5 oz is within 10 % of 15 oz");
            dry.Changes.Should().ContainSingle(c => c.Contains("shadow -> active"));
            (await rig.Db.IngredientContainerSizes.SingleAsync()).Status.Should().Be(ContainerSizeStatus.Shadow, "a dry run writes nothing");
            (await rig.Db.LaneDecisions.CountAsync()).Should().Be(0);

            var applied = await rig.Sizes.ScoreAsync(apply: true, Operator);
            applied.DecisionsRecorded.Should().Be(40);
            var row = await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync();
            row.Status.Should().Be(ContainerSizeStatus.Active);
            row.HoldoutLines.Should().Be(40);
            row.HoldoutAgree.Should().Be(40);
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == ContainerSizeService.StatusChange)).Should().Be(1);
            (await rig.Db.LaneDecisions.CountAsync(d => d.Lane == AiLanes.ContainerSizes && d.AuditOutcome == "pass")).Should().Be(40);
            applied.LaneMode.Should().Be(LaneMode.Shadow, "40 audited comparisons are fewer than a 93 % bound needs");

            var second = await rig.Sizes.ScoreAsync(apply: true, Operator);
            second.DecisionsRecorded.Should().Be(0, "each line is recorded once under a signature");
        }

        [Fact]
        public async Task A_key_whose_size_misses_the_stated_sizes_stays_in_shadow_and_a_lane_qualifies_on_enough_hits()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 190m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(rig, 40, 14.5m);
            var report = await rig.Sizes.ScoreAsync(true, Operator);
            report.HoldoutAgree.Should().Be(0);
            (await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync()).Status.Should().Be(ContainerSizeStatus.Shadow);
            (await rig.Db.LaneDecisions.CountAsync(d => d.AuditOutcome == "error")).Should().Be(40);

            var good = await RigAsync();
            await good.Sizes.WriteAsync(new[] { Proposal(good.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(good, 60, 15m);
            await LinesAsync(good, 60, 15m, "example.com");
            var qualified = await good.Sizes.ScoreAsync(true, Operator);
            qualified.LaneMode.Should().Be(LaneMode.Live, "120 audited hits bound the lane above 93 %");
        }

        [Fact]
        public async Task The_undo_takes_a_run_back_newest_first_with_nothing_skipped_and_a_row_changed_since_is_skipped()
        {
            var rig = await RigAsync();
            var t0 = DateTime.UtcNow.AddSeconds(-1);
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(rig, 40, 15m);
            await rig.Sizes.ScoreAsync(true, Operator);

            var dry = await rig.Sizes.UndoSinceAsync(t0, apply: false, Operator);
            dry.Undone.Should().Be(2);
            dry.Skipped.Should().Be(0);

            var undone = await rig.Sizes.UndoSinceAsync(t0, apply: true, Operator);
            undone.Undone.Should().Be(2);
            undone.Skipped.Should().Be(0);
            (await rig.Db.IngredientContainerSizes.CountAsync()).Should().Be(0, "the row the run added is removed");
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == ContainerSizeService.UndoChange)).Should().Be(2);

            var repeat = await rig.Sizes.UndoSinceAsync(t0, apply: false, Operator);
            repeat.Undone.Should().Be(0);
            repeat.Skipped.Should().Be(0);

            var other = await RigAsync();
            var t1 = DateTime.UtcNow.AddSeconds(-1);
            await other.Sizes.WriteAsync(new[] { Proposal(other.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            var row = await other.Db.IngredientContainerSizes.SingleAsync();
            row.Status = ContainerSizeStatus.Paused;
            await other.Db.SaveChangesAsync();
            var skipped = await other.Sizes.UndoSinceAsync(t1, apply: true, Operator);
            skipped.Skipped.Should().Be(1);
            skipped.Undone.Should().Be(0);
        }

        [Fact]
        public async Task The_lane_undo_takes_back_only_the_scorer_s_status_changes()
        {
            var rig = await RigAsync();
            var t0 = DateTime.UtcNow.AddSeconds(-1);
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(rig, 40, 15m);
            await rig.Sizes.ScoreAsync(true, Operator);

            var result = await new ContainerSizeLaneUndo(rig.Sizes).UndoSinceAsync(LaneTiers.Whole, t0, apply: true, default);
            result.Undone.Should().Be(1);
            var row = await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync();
            row.Status.Should().Be(ContainerSizeStatus.Shadow);
            row.IsDeleted.Should().BeFalse("the size the import wrote stays");
        }

        [Fact]
        public async Task The_lane_undo_leaves_a_key_the_scorer_paused_paused()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(rig, 40, 15m);
            await rig.Sizes.ScoreAsync(true, Operator);
            (await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync()).Status.Should().Be(ContainerSizeStatus.Active);

            var clean = DateTime.UtcNow.AddMilliseconds(1);
            await Task.Delay(5);
            var recipe = new RecipeEntity { Name = "Big can", SourceSite = "example.net", CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            for (var i = 0; i < 3; i++)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = rig.Beans, SortOrder = i, Quantity = 29m, MeasurementId = Ounce, RawLine = "1 (29 oz) can black beans", CreatedDate = Imported.AddDays(1).AddMinutes(i) });
            rig.Db.Recipes.Add(recipe);
            await rig.Db.SaveChangesAsync();
            await rig.Sizes.ScoreAsync(true, Operator);
            (await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync()).Status.Should().Be(ContainerSizeStatus.Paused);

            var result = await new ContainerSizeLaneUndo(rig.Sizes).UndoSinceAsync(LaneTiers.Whole, clean, apply: true, default);
            result.Undone.Should().Be(0, "the lane's undo takes back only keys the scorer made active");
            (await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync()).Status.Should().Be(ContainerSizeStatus.Paused);
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == ContainerSizeService.UndoChange)).Should().Be(0);
        }

        [Fact]
        public async Task An_import_retires_only_rows_of_the_sources_it_read()
        {
            var rig = await RigAsync();
            var portion = Proposal(rig.Beans, 430m, "fdc:175238:portion:2") with { Kind = ContainerKinds.Can, SizeWord = ContainerKinds.Small };
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m), portion }, false, DateTime.UtcNow, true, Operator);

            var withoutBranded = await rig.Sizes.WriteAsync(Array.Empty<ContainerSizeProposal>(), false, DateTime.UtcNow, true, Operator, new[] { "branded-mode" });
            withoutBranded.Retired.Should().Be(1, "the portion's source was read and no longer gives it");
            (await rig.Db.IngredientContainerSizes.Where(r => r.Status != ContainerSizeStatus.Retired).Select(r => r.SourceRef).SingleAsync())
                .Should().StartWith("deterministic:fdc-branded-mode", "a run that did not read the branded files leaves the branded sizes standing");
        }

        [Fact]
        public async Task A_dry_run_of_an_import_scores_its_proposals_without_writing()
        {
            var rig = await RigAsync();
            await LinesAsync(rig, 40, 15m);
            var report = await rig.Sizes.ScoreAsync(true, Operator, new[] { Proposal(rig.Beans, 425m) });
            report.Applied.Should().BeFalse();
            report.HoldoutAgree.Should().Be(40);
            (await rig.Db.IngredientContainerSizes.CountAsync()).Should().Be(0);
            (await rig.Db.LaneDecisions.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_book_recipe_takes_its_era_from_its_source_and_an_undated_book_is_not_scored_against_a_modern_key()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            var book = new RecipeEntity { Name = "Old beans", SourceAttribution = "A Cook Book (1931)", CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            book.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = rig.Beans, Quantity = 15m, MeasurementId = Ounce, RawLine = "1 (15 oz) can black beans", CreatedDate = Imported });
            rig.Db.Recipes.Add(book);
            await rig.Db.SaveChangesAsync();

            var undated = await rig.Sizes.ScoreAsync(false, Operator);
            (undated.LinesWithoutKey, undated.HoldoutLines, undated.OtherEraLines, undated.OtherEraAgree).Should().Be((0, 0, 1, 1),
                "a book with no era row is unknown: it would take the modern size, which is counted apart and not scored against the modern key");
            rig.Db.RecipeSourceEras.Add(new RecipeSourceEraEntity { SourceAttribution = "A Cook Book (1931)", PublishedYear = 1975, Era = ContainerEras.Modern });
            await rig.Db.SaveChangesAsync();
            (await rig.Sizes.ScoreAsync(false, Operator)).HoldoutLines.Should().Be(1);
        }

        private static async Task BookLinesAsync(Rig rig, int count, decimal ounces, string attribution)
        {
            var recipe = new RecipeEntity { Name = "Old beans", SourceAttribution = attribution, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            for (var i = 0; i < count; i++)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    IngredientId = rig.Beans, SortOrder = i, Quantity = ounces, MeasurementId = Ounce,
                    RawLine = $"1 ({ounces} oz) can black beans", CreatedDate = Imported.AddMinutes(i),
                });
            rig.Db.Recipes.Add(recipe);
            await rig.Db.SaveChangesAsync();
        }

        [Fact]
        public async Task Old_book_lines_have_their_own_tier_that_does_not_qualify_while_their_sizes_miss_and_does_when_they_agree()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(rig, 60, 15m);
            await LinesAsync(rig, 60, 15m, "example.com");
            await BookLinesAsync(rig, 120, 20m, "An Undated Book");

            var report = await rig.Sizes.ScoreAsync(true, Operator);

            report.LaneMode.Should().Be(LaneMode.Live, "the modern lines qualify the lane");
            (report.OtherEraLines, report.OtherEraAgree).Should().Be((120, 0));
            report.Tiers.Single(t => t.Tier == ContainerSizeService.ModernTier).Mode.Should().NotBe(LaneMode.Live, "old books state No. 2 cans, which the modern size misses");
            (await rig.Frame.ModeAsync(AiLanes.ContainerSizes, ContainerSizeService.ModernTier)).Should().NotBe(LaneMode.Live);
            (await rig.Db.LaneDecisions.CountAsync(d => d.Tier == ContainerSizeService.ModernTier && d.AuditOutcome == "error")).Should().Be(120);

            var good = await RigAsync();
            await good.Sizes.WriteAsync(new[] { Proposal(good.Beans, 425m) }, false, DateTime.UtcNow, true, Operator);
            await LinesAsync(good, 60, 15m);
            await LinesAsync(good, 60, 15m, "example.com");
            await BookLinesAsync(good, 120, 15m, "An Undated Book");
            (await good.Sizes.ScoreAsync(true, Operator)).Tiers.Single(t => t.Tier == ContainerSizeService.ModernTier).Mode
                .Should().Be(LaneMode.Live, "120 agreeing old-book lines bound the tier above 93 %");
        }

        [Fact]
        public async Task The_lane_undo_of_the_old_book_tier_takes_back_no_key()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m) }, false, DateTime.UtcNow.AddMinutes(-1), true, Operator);
            await LinesAsync(rig, 40, 15m);
            await rig.Sizes.ScoreAsync(true, Operator);
            (await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync()).Status.Should().Be(ContainerSizeStatus.Active);

            var result = await new ContainerSizeLaneUndo(rig.Sizes).UndoSinceAsync(ContainerSizeService.ModernTier, DateTime.UtcNow.AddHours(-1), true, default);

            result.Undone.Should().Be(0);
            (await rig.Db.IngredientContainerSizes.AsNoTracking().SingleAsync()).Status.Should().Be(ContainerSizeStatus.Active);
        }

        [Fact]
        public async Task An_undated_book_line_is_not_scored_against_a_historic_key_as_its_own_era()
        {
            var rig = await RigAsync();
            await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 425m), Proposal(rig.Beans, 567m) with { Era = ContainerEras.Historic } }, false, DateTime.UtcNow, true, Operator);
            await BookLinesAsync(rig, 1, 15m, "An Undated Book");

            var undated = await rig.Sizes.ScoreAsync(false, Operator);
            (undated.HoldoutLines, undated.OtherEraLines).Should().Be((0, 1), "an undated book takes the historic size by fallback, not as its own era");

            rig.Db.RecipeSourceEras.Add(new RecipeSourceEraEntity { SourceAttribution = "An Undated Book", PublishedYear = 1931, Era = ContainerEras.Historic });
            await rig.Db.SaveChangesAsync();
            var dated = await rig.Sizes.ScoreAsync(false, Operator);
            (dated.HoldoutLines, dated.OtherEraLines).Should().Be((1, 0), "a dated old book is the historic key's own era");
        }

        [Fact]
        public async Task The_import_stops_on_a_file_its_checksums_do_not_list_and_writes_a_portion_size_when_they_do()
        {
            var rig = await RigAsync();
            var dir = Directory.CreateTempSubdirectory("usda-").FullName;
            try
            {
                var sr = Directory.CreateDirectory(Path.Combine(dir, "sr")).FullName;
                var foundation = Directory.CreateDirectory(Path.Combine(dir, "foundation")).FullName;
                File.WriteAllLines(Path.Combine(sr, "food.csv"), new[] { "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"", "\"175238\",\"sr_legacy_food\",\"Beans, black, mature seeds, canned, low sodium\",\"16\",\"\"" });
                File.WriteAllLines(Path.Combine(sr, "food_portion.csv"), new[] { "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"portion_description\",\"modifier\",\"gram_weight\",\"data_points\",\"footnote\",\"min_year_acquired\"", "\"1\",\"175238\",\"2\",\"1\",\"9999\",\"\",\"can\",\"430\",\"\",\"\",\"\"" });
                File.WriteAllLines(Path.Combine(foundation, "food.csv"), new[] { "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"" });
                File.WriteAllLines(Path.Combine(foundation, "food_portion.csv"), new[] { "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"portion_description\",\"modifier\",\"gram_weight\",\"data_points\",\"footnote\",\"min_year_acquired\"" });
                var sums = Path.Combine(dir, "sums");
                File.WriteAllText(sums, $"{CatalogAdmissionService.Sha256(Path.Combine(sr, "food.csv"))}  sr/food.csv\n");
                var import = new ContainerSizeImportService(rig.Db, rig.Sizes);

                var stopped = await import.RunAsync(new ContainerSizeImportService.Options(null, sr, foundation, sums, DryRun: false));
                stopped.Stopped.Should().Contain(s => s.Contains("food_portion.csv"));
                (await rig.Db.IngredientContainerSizes.CountAsync()).Should().Be(0);

                File.WriteAllLines(sums, new[] { Path.Combine(sr, "food.csv"), Path.Combine(sr, "food_portion.csv"), Path.Combine(foundation, "food.csv"), Path.Combine(foundation, "food_portion.csv") }
                    .Select((f, i) => $"{CatalogAdmissionService.Sha256(f)}  {(i < 2 ? "sr" : "foundation")}/{Path.GetFileName(f)}"));
                await rig.Sizes.WriteAsync(new[] { Proposal(rig.Beans, 822m) with { SizeWord = ContainerKinds.Large } }, false, DateTime.UtcNow, true, Operator);
                var run = await import.RunAsync(new ContainerSizeImportService.Options(null, sr, foundation, sums, DryRun: false));
                run.Stopped.Should().BeEmpty();
                run.Proposals.Should().ContainSingle(p => p.IngredientId == rig.Beans && p.NetGrams == 430m && p.SourceRef == "fdc:175238:portion:2");
                run.Written!.Retired.Should().Be(0, "a run without the branded files does not retire the branded sizes it did not read");
                (await rig.Db.IngredientContainerSizes.SingleAsync(r => r.SourceRef.StartsWith("fdc:"))).Status.Should().Be(ContainerSizeStatus.Shadow, "no hold-out line yet");
                (await rig.Db.IngredientContainerSizes.SingleAsync(r => r.SizeWord == ContainerKinds.Large)).Status.Should().NotBe(ContainerSizeStatus.Retired);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
