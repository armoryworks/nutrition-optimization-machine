using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The relink undo's dry run in parts that share no row (staging checkpoint 4, P16: one in-memory copy
    /// held every recipe a later entry named, and the T1 dry run took 1,969 s at 440 % CPU). Each part runs
    /// in a copy of its own and the counts add up to what the apply does.
    /// </summary>
    public class RelinkUndoPartsTests
    {
        private static readonly DateTime Imported = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed record Scene(ApplicationDbContext Db, DateTime Started, List<long> Foods);

        private static async Task<Scene> SceneAsync(int recipes)
        {
            var db = ReprocessUndoWorld.NewDb();
            var foods = Enumerable.Range(0, 12).Select(i => new IngredientEntity
            {
                Name = "food " + i, CurationStatusId = (long)CurationStatusEnum.Curated, CreatedDate = Imported,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            }).ToList();
            db.Ingredients.AddRange(foods);
            await db.SaveChangesAsync();
            var rng = new Random(7);
            for (var r = 0; r < recipes; r++)
            {
                db.Recipes.Add(new RecipeEntity
                {
                    Name = "recipe " + r, CurationStatusId = (long)CurationStatusEnum.NonCurated, ScrapedAtUtc = Imported, CreatedDate = Imported,
                    RecipeIngredients = foods.Take(8).OrderBy(_ => rng.Next()).Take(5).Select((f, j) => new RecipeIngredientEntity
                    {
                        IngredientId = f.Id, SortOrder = j, Quantity = 1, MeasurementId = 1, RawLine = f.Name + " line " + j, CreatedDate = Imported,
                    }).ToList(),
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var started = DateTime.UtcNow.AddSeconds(-1);
            var ids = foods.Select(f => f.Id).ToList();

            var relink = new LineRelinkService(db, new ReprocessUndoWorld.Admin());
            (await relink.MoveFoodAsync(ids[0], ids[8]))!.Moved.Should().BeGreaterThan(0);
            db.ChangeTracker.Clear();
            (await relink.MoveFoodAsync(ids[1], ids[9]))!.Moved.Should().BeGreaterThan(0);
            db.ChangeTracker.Clear();
            (await relink.RetireAsync(ids[1], ids[9], takeName: true))!.Retired.Should().BeTrue();
            db.ChangeTracker.Clear();
            (await relink.MoveFoodAsync(ids[2], ids[10]))!.Moved.Should().BeGreaterThan(0);
            db.ChangeTracker.Clear();
            (await relink.MoveFoodAsync(ids[10], ids[11]))!.Moved.Should().BeGreaterThan(0);
            db.ChangeTracker.Clear();

            foreach (var line in (await db.RecipeIngredients.Where(ri => ri.IngredientId == ids[8]).ToListAsync()).Where((_, i) => i % 9 == 0))
            {
                db.RecipeIngredients.Remove(line);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Scene(db, started, ids);
        }

        private static (int, int, int, int, int, int, int, int, int) Counts(LineRelinkUndoResult r) =>
            (r.Restored, r.FoodsRestored, r.Refolded, r.Unsplit, r.AlternativesPutBack, r.PicksRestored, r.ProposalsRestored, r.Skipped, r.SkippedRebuilt);

        [Fact]
        public async Task The_dry_run_in_parts_counts_what_the_apply_does()
        {
            var scene = await SceneAsync(400);
            var entries = await scene.Db.AuditLogEntries.AsNoTracking().Where(a => a.Timestamp >= scene.Started).ToListAsync();
            RelinkUndoDryRun.Parts(entries).Count.Should().BeGreaterThanOrEqualTo(2, "the case must run in more than one copy to mean anything");

            var relink = new LineRelinkService(scene.Db, new ReprocessUndoWorld.Admin());
            var dry = await relink.UndoAsync(scene.Started);
            scene.Db.ChangeTracker.Clear();
            var applied = await relink.UndoAsync(scene.Started, apply: true);

            dry.Applied.Should().BeFalse();
            Counts(dry).Should().Be(Counts(applied));
            (dry.Restored, dry.FoodsRestored, dry.Skipped).Should().Match<(int Restored, int Foods, int Skipped)>(c => c.Restored > 500 && c.Foods > 0 && c.Skipped > 0);
            dry.DryRunLargestPart.Should().BeLessThanOrEqualTo(100, "staging checkpoint 6, R15: the recipes whose lines name a retired food no longer share one large copy");
            dry.DryRunParts.Should().Be(RelinkUndoDryRun.Parts(entries).Count);
        }

        [Fact]
        public async Task Two_dry_runs_called_together_run_one_after_the_other_and_the_second_says_it_waited()
        {
            var first = await SceneAsync(300);
            var second = await SceneAsync(300);
            RelinkUndoDryRun.ResetMostRunning();

            var together = await Task.WhenAll(
                Task.Run(() => new LineRelinkService(first.Db, new ReprocessUndoWorld.Admin()).UndoAsync(first.Started)),
                Task.Run(() => new LineRelinkService(second.Db, new ReprocessUndoWorld.Admin()).UndoAsync(second.Started)));

            RelinkUndoDryRun.MostRunning.Should().Be(1, "staging checkpoint 6, R15: T1 and T7 took turns part by part and took 636 s and 425 s; whole runs now queue");
            var later = together.OrderByDescending(r => r.QueuedSeconds).First();
            later.QueuedBehind.Should().BeGreaterThanOrEqualTo(1, "the run that started second is told it ran behind the other");
            later.QueuedSeconds.Should().BeGreaterThan(0, "it waited for the whole of the other run");
            Counts(together[0]).Should().Be(Counts(together[1]), "both scenes are built alike");
        }

        [Fact]
        public async Task Two_dry_runs_called_together_take_turns_one_copy_at_a_time_and_each_counts_what_it_counts_alone()
        {
            var first = await SceneAsync(400);
            var second = await SceneAsync(400);
            var alone = await new LineRelinkService(first.Db, new ReprocessUndoWorld.Admin()).UndoAsync(first.Started);
            first.Db.ChangeTracker.Clear();
            RelinkUndoDryRun.ResetMostOpen();

            var together = await Task.WhenAll(
                Task.Run(() => new LineRelinkService(first.Db, new ReprocessUndoWorld.Admin()).UndoAsync(first.Started)),
                Task.Run(() => new LineRelinkService(second.Db, new ReprocessUndoWorld.Admin()).UndoAsync(second.Started)));

            RelinkUndoDryRun.MostOpen.Should().Be(1, "staging checkpoint 5, Q20: T1 and T7 beside it took the API to 367 % CPU; the copies now run one at a time in the process");
            Counts(together[0]).Should().Be(Counts(alone));
            Counts(together[1]).Should().Be(Counts(alone), "both scenes are built alike");
        }

        [Fact]
        public async Task Every_recipe_lies_whole_in_one_part_and_the_first_part_holds_the_retirements_and_no_line()
        {
            var scene = await SceneAsync(400);
            var entries = await scene.Db.AuditLogEntries.AsNoTracking().Where(a => a.Timestamp >= scene.Started).ToListAsync();

            var parts = RelinkUndoDryRun.Parts(entries, size: 100);

            parts.SelectMany(p => p).Should().BeEquivalentTo(entries, o => o.ComparingByMembers<AuditLogEntryEntity>());
            var lines = entries.Where(e => e.EntityType == LineRelinkService.RelinkEntityType).ToList();
            lines.GroupBy(e => e.EntityId).Should().OnlyContain(recipe => parts.Count(p => p.Any(e => e.EntityType == LineRelinkService.RelinkEntityType && e.EntityId == recipe.Key)) == 1);
            var foodParts = parts.TakeWhile(p => p.All(e => e.EntityType != LineRelinkService.RelinkEntityType)).ToList();
            foodParts.SelectMany(p => p).Should().BeEquivalentTo(entries.Where(e => e.EntityType != LineRelinkService.RelinkEntityType), o => o.ComparingByMembers<AuditLogEntryEntity>(),
                "staging checkpoint 6, R15: the first part held every recipe whose lines name a retired food, most of T1");
            var retired = scene.Foods[1].ToString(System.Globalization.CultureInfo.InvariantCulture) + "@";
            parts.Skip(1).SelectMany(p => p).Should().Contain(e => (e.OldValue ?? string.Empty).StartsWith(retired, StringComparison.Ordinal));
            parts.Should().OnlyContain(p => p.Count <= 100);
        }

        [Fact]
        public async Task The_food_entries_run_in_parts_of_at_most_the_part_size_each_retirement_whole_and_count_what_the_apply_does()
        {
            var scene = await SceneAsync(150);
            var db = scene.Db;
            var extra = Enumerable.Range(0, 130).Select(i => new IngredientEntity
            {
                Name = "merged " + i, CurationStatusId = (long)CurationStatusEnum.Curated, CreatedDate = Imported, GramsPerMilliliter = i < 65 ? 1.03m : null,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            }).ToList();
            db.Ingredients.AddRange(extra);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var relink = new LineRelinkService(db, new ReprocessUndoWorld.Admin());
            for (var i = 0; i < 60; i++)
            {
                (await relink.RetireAsync(extra[i].Id, extra[65 + i].Id, takeName: true))!.Retired.Should().BeTrue();
                db.ChangeTracker.Clear();
            }
            (await relink.RetireAsync(extra[65].Id, extra[125].Id, takeName: true))!.Retired.Should().BeTrue();
            db.ChangeTracker.Clear();
            var entries = await db.AuditLogEntries.AsNoTracking().Where(a => a.Timestamp >= scene.Started).ToListAsync();
            entries.Count(e => e.EntityType != LineRelinkService.RelinkEntityType).Should().BeGreaterThan(100, "the case must hold more food entries than one part");

            var parts = RelinkUndoDryRun.Parts(entries);
            parts.Should().OnlyContain(p => p.Count <= 100, "review of ck11-small: the first part held every retirement, alias, measure and pick entry of the window with no limit");
            var chained = new[] { extra[0].Id, extra[65].Id, extra[125].Id };
            parts.Count(p => p.Any(e => e.EntityType == LineRelinkService.RetireEntityType && chained.Contains(e.EntityId))).Should().Be(1, "a retirement and the one that retired its food again share a food and a name");
            parts.SkipWhile(p => p.All(e => e.EntityType != LineRelinkService.RelinkEntityType)).Should().OnlyContain(p => p.All(e => e.EntityType == LineRelinkService.RelinkEntityType));

            var dry = await relink.UndoAsync(scene.Started);
            db.ChangeTracker.Clear();
            var applied = await relink.UndoAsync(scene.Started, apply: true);

            Counts(dry).Should().Be(Counts(applied));
            dry.FoodsRestored.Should().BeGreaterThan(60);
            dry.DryRunLargestPart.Should().BeLessThanOrEqualTo(100);
        }
    }
}
