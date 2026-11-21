using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Book years read from title pages are stored per book source with their era, journaled
    /// under one stamp and undone by it; a source no book recipe carries, or an impossible year,
    /// is refused, and a dry run writes nothing.
    /// </summary>
    public class RecipeSourceEraServiceTests
    {
        private const long Operator = 1;
        private const string Plain = "A Plain Cook Book (Example 1901)";
        private const string Second = "A Second Plain Cook Book (Example 1937)";

        private static async Task<(ApplicationDbContext Db, RecipeSourceEraService Eras)> RigAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Recipes.AddRange(
                new RecipeEntity { Name = "Seed Cake", SourceAttribution = Plain },
                new RecipeEntity { Name = "Boiled Rice", SourceAttribution = Plain },
                new RecipeEntity { Name = "Corn Pudding", SourceAttribution = Second },
                new RecipeEntity { Name = "Web chili", SourceSite = "example.com", SourceAttribution = "Example Kitchen" });
            await db.SaveChangesAsync();
            return (db, new RecipeSourceEraService(db, NullLogger<RecipeSourceEraService>.Instance));
        }

        private static RecipeSourceEraInput Year(string source, int? year, string? where = "title-page:line 4") =>
            new() { SourceAttribution = source, PublishedYear = year, YearSource = where };

        [Fact]
        public async Task A_dry_run_lists_what_it_would_write_and_writes_nothing()
        {
            var (db, eras) = await RigAsync();

            var report = await eras.SetAsync(new[] { Year(Plain, 1901), Year(Second, 1937) }, apply: false, Operator);

            (report.Applied, report.Added, report.Changed).Should().Be((false, 2, 0));
            report.ByEra[ContainerEras.Historic].Should().Be(2);
            (await db.RecipeSourceEras.CountAsync()).Should().Be(0);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task An_apply_stores_each_year_with_its_era_and_journals_it_under_one_stamp()
        {
            var (db, eras) = await RigAsync();

            var report = await eras.SetAsync(new[] { Year(Plain, 1901), Year(Second, 1964, "pg-header:original-publication"), Year("Example Kitchen", 2001), Year("Nobody's Book", 1920), Year(Plain, 1902) }, apply: true, Operator);

            (report.Added, report.Refused.Count).Should().Be((2, 3));
            report.Refused.Should().Contain(r => r.StartsWith("Example Kitchen")).And.Contain(r => r.StartsWith("Nobody's Book")).And.Contain(r => r.Contains("given twice"));
            var stored = await db.RecipeSourceEras.AsNoTracking().ToDictionaryAsync(e => e.SourceAttribution);
            (stored[Plain].PublishedYear, stored[Plain].Era).Should().Be((1901, ContainerEras.Historic));
            (stored[Second].Era, stored[Second].YearSource).Should().Be((ContainerEras.Modern, "pg-header:original-publication"));
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == RecipeSourceEraService.SetChange).ToListAsync();
            journal.Should().HaveCount(2).And.OnlyContain(a => a.Timestamp == report.Stamp && a.OldValue == null);

            (await eras.SetAsync(new[] { Year(Plain, 1901) }, apply: true, Operator)).Unchanged.Should().Be(1);
            (await eras.SetAsync(new[] { Year(Plain, 1500) }, apply: false, Operator)).Refused.Should().ContainSingle().Which.Should().Contain("outside");

            var list = await eras.ListAsync();
            list.Items.Select(i => (i.SourceAttribution, i.Recipes, i.Era)).Should().Equal((Plain, 2, ContainerEras.Historic), (Second, 1, ContainerEras.Modern));
        }

        [Fact]
        public async Task The_undo_by_stamp_restores_each_source_skips_one_changed_since_and_a_rewrite_revives_a_removed_row()
        {
            var (db, eras) = await RigAsync();
            var first = await eras.SetAsync(new[] { Year(Plain, 1901) }, apply: true, Operator);
            await Task.Delay(5);
            var second = await eras.SetAsync(new[] { Year(Plain, 1911), Year(Second, null, "title-page: no single year") }, apply: true, Operator);

            var dry = await eras.UndoAsync(second.Stamp, apply: false, Operator);
            (dry.Undone, dry.Skipped).Should().Be((2, 0));
            (await db.RecipeSourceEras.AsNoTracking().SingleAsync(e => e.SourceAttribution == Plain)).PublishedYear.Should().Be(1911, "a dry run writes nothing");

            var undo = await eras.UndoAsync(second.Stamp, apply: true, Operator);
            (undo.Undone, undo.Skipped).Should().Be((2, 0));
            (await db.RecipeSourceEras.AsNoTracking().SingleAsync(e => e.SourceAttribution == Plain)).PublishedYear.Should().Be(1901);
            (await db.RecipeSourceEras.AsNoTracking().AnyAsync(e => e.SourceAttribution == Second)).Should().BeFalse();
            (await eras.UndoAsync(second.Stamp, apply: true, Operator)).Skipped.Should().Be(2, "both rows already hold what the undo gave them");

            await Task.Delay(5);
            var again = await eras.SetAsync(new[] { Year(Second, 1937) }, apply: true, Operator);
            again.Added.Should().Be(1);
            (await db.RecipeSourceEras.IgnoreQueryFilters().CountAsync(e => e.SourceAttribution == Second)).Should().Be(1, "the removed row is revived, so the unique source holds");

            var plain = await db.RecipeSourceEras.SingleAsync(e => e.SourceAttribution == Plain);
            plain.PublishedYear = 1899;
            await db.SaveChangesAsync();
            (await eras.UndoAsync(first.Stamp, apply: true, Operator)).Skipped.Should().Be(1, "the year was changed since");
        }
    }
}
