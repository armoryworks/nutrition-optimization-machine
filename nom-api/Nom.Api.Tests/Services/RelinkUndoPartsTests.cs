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
        }

        [Fact]
        public async Task Every_recipe_lies_whole_in_one_part_and_the_first_part_holds_every_retirement_with_the_recipes_whose_lines_name_its_food()
        {
            var scene = await SceneAsync(400);
            var entries = await scene.Db.AuditLogEntries.AsNoTracking().Where(a => a.Timestamp >= scene.Started).ToListAsync();

            var parts = RelinkUndoDryRun.Parts(entries, size: 100);

            parts.SelectMany(p => p).Should().BeEquivalentTo(entries, o => o.ComparingByMembers<AuditLogEntryEntity>());
            var lines = entries.Where(e => e.EntityType == LineRelinkService.RelinkEntityType).ToList();
            lines.GroupBy(e => e.EntityId).Should().OnlyContain(recipe => parts.Count(p => p.Any(e => e.EntityType == LineRelinkService.RelinkEntityType && e.EntityId == recipe.Key)) == 1);
            parts[0].Should().Contain(entries.Where(e => e.EntityType != LineRelinkService.RelinkEntityType));
            var retired = scene.Foods[1].ToString(System.Globalization.CultureInfo.InvariantCulture) + "@";
            parts.Skip(1).SelectMany(p => p).Should().NotContain(e => (e.OldValue ?? string.Empty).StartsWith(retired, StringComparison.Ordinal));
            parts.Skip(1).Should().OnlyContain(p => p.Count <= 100);
        }
    }
}
