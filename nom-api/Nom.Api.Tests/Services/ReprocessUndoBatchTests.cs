using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The reprocess undo in batches (staging checkpoint 4, P20: the order-only pass's undo took back about
    /// 80 recipes a minute, about 20 hours for 97,400, and could only be ended by a restart). The generated
    /// copy of the pass (<see cref="ReprocessUndoWorld"/>) was undone by the row-by-row undo of
    /// cleanup2/integration at 93bb4b15, and its every row, journal, audit row and label value is
    /// Fixtures/ReprocessUndo/row-by-row-undo.tsv; each batched run here must leave exactly that.
    /// </summary>
    public class ReprocessUndoBatchTests
    {
        private const int Recipes = 160;
        private const int Seed = 20261123;

        private static readonly string RowByRow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReprocessUndo", "row-by-row-undo.tsv")).Replace("{line-builder}", Nom.Orch.Services.Support.RecipeLineBuilder.Version, StringComparison.Ordinal);

        private static (string Dry, string Applied, string Dump) Expected()
        {
            var lines = RowByRow.Split('\n', 3);
            return (lines[0], lines[1], lines[2]);
        }

        private static RecipeReprocessUndoRequest All(bool apply, int batchSize = RecipeReprocessService.UndoBatchSize, int? maxBatches = null) =>
            new() { SinceUtc = ReprocessUndoWorld.Since, Apply = apply, BatchSize = batchSize, MaxBatches = maxBatches };

        [Theory]
        [InlineData(500)]
        [InlineData(7)]
        [InlineData(1)]
        public async Task The_batched_undo_leaves_every_row_as_the_row_by_row_undo_did(int batchSize)
        {
            var world = await ReprocessUndoWorld.GenerateAsync(Recipes, Seed);
            var expected = Expected();

            var dry = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: false, batchSize));
            var undoStarted = DateTime.UtcNow;
            var applied = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize));

            ReprocessUndoWorld.Counts(dry).Should().Be(expected.Dry, "a dry run judges each entry as the row-by-row dry run did");
            ReprocessUndoWorld.Counts(applied).Should().Be(expected.Applied);
            applied.Batches.Should().Be((applied.Entries + batchSize - 1) / batchSize);
            (applied.Remaining, applied.Stopped, applied.Refused).Should().Be((0, (string?)null, (string?)null));
            (await ReprocessUndoWorld.DumpAsync(world.Db, world.PassStarted, undoStarted)).Should().Be(expected.Dump);
        }

        [Fact]
        public async Task A_run_stopped_between_batches_goes_on_under_its_id_and_ends_where_one_whole_run_ends()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(Recipes, Seed);
            var expected = Expected();
            var undoStarted = DateTime.UtcNow;

            var first = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize: 10, maxBatches: 3));
            first.Stopped.Should().Be(RecipeReprocessUndoStop.MaxBatches);
            (first.Batches, first.Remaining).Should().Be((3, first.Entries - 30));
            first.RunId.Should().NotBeNullOrEmpty();

            var stopped = await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(first.RunId);
            stopped!.State.Should().Be(RecipeReprocessUndoProgress.States.Stopped);
            (stopped.Batches, stopped.Reached, stopped.EntriesAtStart).Should().Be((3, 30, first.Entries));
            stopped.Remaining.Should().Be(first.Remaining, "every entry below the last one reached is still standing");
            stopped.Restored.Should().Be(first.Restored);

            var rest = await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest { RunId = first.RunId, Apply = true, BatchSize = 25 });
            rest.Refused.Should().BeNull();
            (rest.RunId, rest.Entries, rest.Remaining).Should().Be((first.RunId, first.Remaining, 0));

            var finished = await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(null);
            finished!.RunId.Should().Be(first.RunId);
            finished.State.Should().Be(RecipeReprocessUndoProgress.States.Finished);
            (finished.Reached, finished.Remaining).Should().Be((first.Entries, 0));
            ReprocessUndoWorld.Counts(new RecipeReprocessUndoResult
            {
                Entries = finished.EntriesAtStart, Restored = finished.Restored, Skipped = first.Skipped + rest.Skipped, StateKept = finished.StateKept,
                PositionsFollowed = finished.PositionsFollowed, FoodsRemoved = finished.FoodsRemoved, FoodsKept = finished.FoodsKept,
                RestoredByKind = finished.RestoredByKind, SkippedRecipeIds = first.SkippedRecipeIds.Concat(rest.SkippedRecipeIds).ToList(),
            }).Should().Be(expected.Applied);
            finished.Skipped.Should().Be(first.Skipped + rest.Skipped);
            (await ReprocessUndoWorld.DumpAsync(world.Db, world.PassStarted, undoStarted)).Should().Be(expected.Dump);

            var again = await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest { RunId = first.RunId, Apply = true });
            again.Refused.Should().Contain("finished");
        }

        [Fact]
        public async Task A_run_asked_to_stop_ends_before_its_next_batch_and_a_run_whose_call_ended_without_an_end_row_is_interrupted()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(40, Seed);
            var first = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize: 5, maxBatches: 1));
            world.Db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = RecipeReprocessService.UndoRunEntityType, ChangeType = RecipeReprocessService.UndoStopChangeType, PropertyName = first.RunId,
                Timestamp = DateTime.UtcNow.AddHours(1), ChangedByPersonId = ReprocessUndoWorld.Operator,
            });
            await world.Db.SaveChangesAsync();
            world.Db.ChangeTracker.Clear();

            var asked = await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest { RunId = first.RunId, Apply = true, BatchSize = 5 });

            (asked.Stopped, asked.Batches, asked.Restored).Should().Be((RecipeReprocessUndoStop.StopRequested, 0, 0));
            var progress = await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(first.RunId);
            (progress!.State, progress.Outcome, progress.StopRequested).Should().Be((RecipeReprocessUndoProgress.States.Stopped, RecipeReprocessUndoStop.StopRequested, true));
            progress.Batches.Should().Be(1);

            world.Db.AuditLogEntries.RemoveRange(await world.Db.AuditLogEntries.Where(a => a.EntityType == RecipeReprocessService.UndoRunEntityType
                && (a.ChangeType == RecipeReprocessService.UndoRunEndChangeType || a.ChangeType == RecipeReprocessService.UndoStopChangeType)).ToListAsync());
            await world.Db.SaveChangesAsync();
            world.Db.ChangeTracker.Clear();
            (await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(first.RunId))!.State.Should().Be(RecipeReprocessUndoProgress.States.Interrupted);

            var rest = await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest { RunId = first.RunId, Apply = true, BatchSize = 5 });
            (rest.Stopped, rest.Remaining, rest.Entries).Should().Be(((string?)null, 0, first.Remaining));
            (await world.Db.RecipeReprocessJournals.CountAsync(j => j.UndoneAt == null && !j.BatchId.StartsWith(RecipeReprocessService.OrderFollowPrefix)))
                .Should().Be(first.Skipped + rest.Skipped);
        }

        [Fact]
        public async Task A_caller_that_went_away_ends_the_run_before_its_next_batch_and_every_batch_written_is_whole()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(40, Seed);
            using var gone = new CancellationTokenSource();
            gone.Cancel();

            var result = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize: 5), gone.Token);

            (result.Stopped, result.Batches, result.Restored, result.Remaining).Should().Be((RecipeReprocessUndoStop.ClientGone, 0, 0, result.Entries));
            (await world.Db.RecipeReprocessJournals.CountAsync(j => j.UndoneAt != null)).Should().Be(0);
            (await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(result.RunId))!.State.Should().Be(RecipeReprocessUndoProgress.States.Stopped);
        }

        [Fact]
        public async Task Each_batch_of_an_apply_writes_one_progress_row_whose_counts_add_up_to_the_answer()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(60, Seed);

            var result = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize: 9));

            var rows = await world.Db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RecipeReprocessService.UndoRunEntityType && a.PropertyName == result.RunId).OrderBy(a => a.Id).ToListAsync();
            rows.Select(r => r.ChangeType).Should().Equal(new[] { RecipeReprocessService.UndoRunChangeType }
                .Concat(Enumerable.Repeat(RecipeReprocessService.UndoBatchChangeType, result.Batches))
                .Append(RecipeReprocessService.UndoRunEndChangeType));
            rows.Where(r => r.ChangeType == RecipeReprocessService.UndoBatchChangeType).Select(r => r.EntityId).Should().Equal(Enumerable.Range(1, result.Batches).Select(i => (long)i));
            var progress = await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(result.RunId);
            (progress!.Reached, progress.Restored, progress.Skipped, progress.PositionsFollowed, progress.FoodsRemoved)
                .Should().Be((result.Entries, result.Restored, result.Skipped, result.PositionsFollowed, result.FoodsRemoved));
            progress.RestoredByKind.Should().BeEquivalentTo(result.RestoredByKind);
            progress.LastEntryId.Should().Be(await world.Db.RecipeReprocessJournals.Where(j => j.CreatedDate >= ReprocessUndoWorld.Since
                && !j.BatchId.StartsWith(RecipeReprocessService.OrderFollowPrefix)).MinAsync(j => j.Id));
        }

        [Fact]
        public async Task A_second_operator_run_is_refused_while_one_is_going_and_a_dry_run_writes_no_progress_row()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(20, Seed);
            var dry = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: false));
            dry.RunId.Should().BeNull();
            (await world.Db.AuditLogEntries.CountAsync(a => a.EntityType == RecipeReprocessService.UndoRunEntityType)).Should().Be(0);

            var unknown = await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest { RunId = "ru-none", Apply = true });
            unknown.Refused.Should().Contain("no undo run");
        }

        private static RecipeReprocessService WithLabels(ApplicationDbContext db, IRecipeNutritionService nutrition) =>
            new(db, new ReprocessUndoWorld.Admin(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(), nutrition, NullLogger<RecipeReprocessService>.Instance);

        [Fact]
        public async Task Every_recipe_whose_lines_an_undo_put_back_gets_its_label_calculated_again_and_one_whose_positions_it_only_followed_does_not()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(60, Seed);
            var nutrition = new Mock<IRecipeNutritionService>();
            var calculated = new List<long>();
            nutrition.Setup(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .Callback<IReadOnlyCollection<long>>(ids => calculated.AddRange(ids))
                .ReturnsAsync(0);
            var kindGivenSince = await world.Db.RecipeIngredients.Where(ri => ri.LineKind != null).Select(ri => ri.RecipeId).Distinct().ToListAsync();

            var applied = await WithLabels(world.Db, nutrition.Object).UndoAsync(All(apply: true, batchSize: 9));

            var undone = await world.Db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RecipeReprocessService.RecipeEntityType && a.ChangeType == RecipeReprocessService.UndoChangeType && a.NewValue != "positions followed")
                .Select(a => a.OldValue!)
                .ToListAsync();
            var journalIds = undone.Select(v => long.Parse(v.Split(';')[0]["journal=".Length..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var linesPutBack = await world.Db.RecipeReprocessJournals.AsNoTracking()
                .Where(j => journalIds.Contains(j.Id) && j.OldRows != null && j.OldRows != j.NewRows)
                .Select(j => j.RecipeId)
                .Distinct()
                .ToListAsync();
            applied.PositionsFollowed.Should().BeGreaterThan(0);
            linesPutBack.Should().NotBeEmpty();
            calculated.Distinct().Should().BeEquivalentTo(linesPutBack,
                "a label also reads its foods' current nutrients, container sizes and servings, so the row-by-row undo calculated it after every restore whatever the lines held (review CK9US-1)");
            linesPutBack.Except(kindGivenSince).Should().NotBeEmpty("recipes whose restored lines differ only in position are calculated too");
            nutrition.Verify(n => n.RecalculateAsync(It.IsAny<long>()), Times.Never(), "a batch's labels are calculated together");
        }

        [Fact]
        public async Task A_batch_calculates_its_labels_before_its_progress_row_so_both_are_committed_together()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(40, Seed);
            var nutrition = new Mock<IRecipeNutritionService>();
            var progressRowsAtLabels = new List<int>();
            nutrition.Setup(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .Returns(async () =>
                {
                    progressRowsAtLabels.Add(await world.Db.AuditLogEntries.CountAsync(a => a.EntityType == RecipeReprocessService.UndoRunEntityType
                        && a.ChangeType == RecipeReprocessService.UndoBatchChangeType));
                    return 0;
                });

            var applied = await WithLabels(world.Db, nutrition.Object).UndoAsync(All(apply: true));

            applied.Batches.Should().Be(1);
            progressRowsAtLabels.Should().Equal(new[] { 0 }, "a restart while labels are calculated must leave the batch unwritten, not marked done with old labels (review CK9US-2)");
            (await world.Db.AuditLogEntries.CountAsync(a => a.EntityType == RecipeReprocessService.UndoRunEntityType && a.ChangeType == RecipeReprocessService.UndoBatchChangeType))
                .Should().Be(1);
        }

        [Fact]
        public async Task An_entry_taken_back_by_something_else_after_the_run_listed_it_is_not_judged_again()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(Recipes, Seed);
            var listed = await world.Db.RecipeReprocessJournals.AsNoTracking()
                .Where(j => j.CreatedDate >= ReprocessUndoWorld.Since && j.UndoneAt == null && !j.BatchId.StartsWith(RecipeReprocessService.OrderFollowPrefix))
                .OrderBy(j => j.Id)
                .Select(j => j.Id)
                .Take(5)
                .ToListAsync();
            var elsewhere = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            var nutrition = new Mock<IRecipeNutritionService>();
            var marked = false;
            nutrition.Setup(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .Returns(async () =>
                {
                    if (marked) return 0;
                    marked = true;
                    foreach (var entry in await world.Db.RecipeReprocessJournals.Where(j => listed.Contains(j.Id)).ToListAsync()) entry.UndoneAt = elsewhere;
                    await world.Db.SaveChangesAsync();
                    return 0;
                });

            var applied = await WithLabels(world.Db, nutrition.Object).UndoAsync(All(apply: true, batchSize: 10));

            marked.Should().BeTrue();
            applied.AlreadyUndone.Should().Be(listed.Count, "review CK9US-3: a second run must not take the same entries back again");
            (applied.Restored + applied.Skipped + applied.AlreadyUndone).Should().Be(applied.Entries);
            (await world.Db.RecipeReprocessJournals.Where(j => listed.Contains(j.Id)).Select(j => j.UndoneAt).ToListAsync()).Should().OnlyContain(at => at == elsewhere);
            var markers = listed.Select(id => "journal=" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";").ToList();
            (await world.Db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == RecipeReprocessService.UndoChangeType).Select(a => a.OldValue!).ToListAsync())
                .Should().NotContain(v => markers.Any(m => v.StartsWith(m)));
            (await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(applied.RunId))!.AlreadyUndone.Should().Be(listed.Count);
        }

        [Fact]
        public async Task While_an_apply_runs_another_operator_apply_is_refused_and_the_lane_undo_waits_for_it()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(40, Seed);
            var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var nutrition = new Mock<IRecipeNutritionService>();
            nutrition.Setup(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .Returns(async () =>
                {
                    inside.TrySetResult();
                    await release.Task;
                    return 0;
                });
            var running = WithLabels(world.Db, nutrition.Object).UndoAsync(All(apply: true));
            await inside.Task.WaitAsync(TimeSpan.FromSeconds(30));

            await using var other = world.OtherContext();
            var second = await ReprocessUndoWorld.Service(other).UndoAsync(All(apply: true));
            second.Refused.Should().Contain("is going");

            await using var laneContext = world.OtherContext();
            var lane = ReprocessUndoWorld.Service(laneContext).UndoAsync(ReprocessUndoWorld.Since, null, RecipeReprocessKind.Order, apply: true, laneOnly: true);
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            lane.IsCompleted.Should().BeFalse("review CK9US-3: the lane's own undo may not take entries back while another apply is judging them");

            release.SetResult();
            var first = await running;
            var laneResult = await lane.WaitAsync(TimeSpan.FromSeconds(30));
            (first.Refused, laneResult.Refused).Should().Be(((string?)null, (string?)null));
            laneResult.RunId.Should().NotBe(first.RunId);
        }

        [Fact]
        public async Task The_rate_counts_only_the_time_the_batches_took_not_the_time_between_a_stop_and_the_call_that_goes_on()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(40, Seed);
            var first = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize: 10, maxBatches: 2));
            var start = await world.Db.AuditLogEntries.SingleAsync(a => a.EntityType == RecipeReprocessService.UndoRunEntityType
                && a.ChangeType == RecipeReprocessService.UndoRunChangeType && a.PropertyName == first.RunId);
            start.Timestamp = start.Timestamp.AddMinutes(-30);
            await world.Db.SaveChangesAsync();
            world.Db.ChangeTracker.Clear();

            await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest { RunId = first.RunId, Apply = true, BatchSize = 10 });
            var progress = await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(first.RunId);

            progress!.State.Should().Be(RecipeReprocessUndoProgress.States.Finished);
            progress.WorkingSeconds.Should().BeGreaterThan(0).And.BeLessThan(60);
            progress.EntriesPerMinute.Should().BeGreaterThan(progress.Reached, "review CK9US-4: the half hour the run stood stopped is not working time");
        }

        [Fact]
        public async Task Progress_and_stop_without_a_run_id_take_the_newest_operator_run_and_not_the_lanes_own()
        {
            var world = await ReprocessUndoWorld.GenerateAsync(40, Seed);
            var operatorRun = await ReprocessUndoWorld.Service(world.Db).UndoAsync(All(apply: true, batchSize: 5, maxBatches: 1));
            var laneRun = await ReprocessUndoWorld.Service(world.Db).UndoAsync(new RecipeReprocessUndoRequest
            {
                SinceUtc = ReprocessUndoWorld.Since, Kind = RecipeReprocessKind.Order, Apply = true, LaneOnly = true,
            });
            laneRun.RunId.Should().NotBeNullOrEmpty().And.NotBe(operatorRun.RunId);

            (await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(null))!.RunId.Should().Be(operatorRun.RunId, "review CK9US-5");
            var stopped = await ReprocessUndoWorld.Service(world.Db).StopUndoAsync(null);
            (stopped!.RunId, stopped.StopRequested).Should().Be((operatorRun.RunId!, true));
            (await ReprocessUndoWorld.Service(world.Db).UndoProgressAsync(laneRun.RunId))!.LaneOnly.Should().BeTrue();
        }

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public List<(string Sql, List<NpgsqlParameter> Parameters)> Commands { get; } = new();

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Commands.Add((command.CommandText, command.Parameters.Cast<NpgsqlParameter>().ToList()));
                return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
            }
        }

        private sealed class NoConnection : DbConnectionInterceptor
        {
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(InterceptionResult.Suppress());
        }

        [Fact]
        public async Task A_batch_of_lines_is_written_on_Postgres_with_one_delete_and_one_typed_insert()
        {
            var capture = new CaptureAndStop();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var at = new DateTime(2026, 11, 23, 9, 15, 15, DateTimeKind.Utc);
            var rows = new Dictionary<long, List<RecipeReprocessService.LineRow>>
            {
                [7] = new() { new(11, 0, 2m, 3, "2 cups flour", null, null, at, 2, null, null, false, null, null), new(12, 1, 1m, 3, "1 cup milk", "ingredient", 12, at, 2, at, 1, false, null, null, 0.85m, "timed:simmer:300") },
                [8] = new() { new(13, 0, 3m, 1, "3 eggs", null, null, DateTime.SpecifyKind(at, DateTimeKind.Unspecified), null, null, null, true, at, 2) },
            };

            await RecipeReprocessService.ReplaceRowsAsync(db, rows);

            capture.Commands.Should().HaveCount(2);
            capture.Commands[0].Sql.Should().Contain("DELETE FROM recipe.\"RecipeIngredient\"").And.Contain("ANY(");
            var insert = capture.Commands[1];
            insert.Sql.Should().Contain("INSERT INTO recipe.\"RecipeIngredient\"").And.Contain("unnest(");
            var parameters = insert.Parameters;
            parameters.Should().HaveCount(17);
            parameters.Should().OnlyContain(p => (p.NpgsqlDbType & NpgsqlDbType.Array) == NpgsqlDbType.Array);
            ((long[])parameters.Single(p => p.ParameterName == "recipe").Value!).Should().Equal(7, 7, 8);
            ((int[])parameters.Single(p => p.ParameterName == "position").Value!).Should().Equal(0, 1, 0);
            ((long?[])parameters.Single(p => p.ParameterName == "entity").Value!).Should().Equal(null, 12, null);
            ((DateTime[])parameters.Single(p => p.ParameterName == "created").Value!).Should().OnlyContain(d => d.Kind == DateTimeKind.Utc && d == at);
            ((DateTime?[])parameters.Single(p => p.ParameterName == "deletedAt").Value!).Should().Equal(null, null, at);
            ((decimal?[])parameters.Single(p => p.ParameterName == "retention").Value!).Should().Equal(null, 0.85m, null);
            parameters.Single(p => p.ParameterName == "created").NpgsqlDbType.Should().Be(NpgsqlDbType.Array | NpgsqlDbType.TimestampTz);
        }
    }
}
