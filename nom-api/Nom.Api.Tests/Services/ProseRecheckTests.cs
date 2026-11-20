using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Recipes the rewrite lane already took out of quarantine are checked against their stored
    /// source; the ones that fail go back into quarantine with their curation status untouched,
    /// and the undo takes them out again.
    /// </summary>
    public class ProseRecheckTests
    {
        private const long Admin = 77;
        private static readonly DateTime Imported = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Rewritten = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

        private const string Source = """
            {"@type":"Recipe","description":"Crisp roast potatoes for a weeknight.",
             "recipeInstructions":[
               {"@type":"HowToStep","text":"Preheat the oven to 425 degrees F."},
               {"@type":"HowToStep","text":"Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy."}]}
            """;

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

        private static async Task<long> SeedAsync(
            ApplicationDbContext db, CurationStatusEnum status, string[] steps, string? source = Source,
            bool quarantined = false, string license = RecipeLicenseStatus.Unknown, long? editedBy = null)
        {
            var recipe = new RecipeEntity
            {
                Name = "Roast potatoes",
                Description = "Weeknight potatoes, roasted until crisp.",
                ScrapedAtUtc = Imported,
                LicenseStatus = license,
                ContainsSourceProse = quarantined,
                ProseRewriteAttemptedAt = Rewritten,
                CurationStatusId = (long)status,
                RecipeSteps = steps.Select((text, i) => new RecipeStepEntity
                {
                    StepNumber = i + 1, Description = text, CreatedDate = Imported,
                    LastModifiedByPersonId = i == 0 ? editedBy ?? SystemConstants.SystemPersonId : SystemConstants.SystemPersonId,
                }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            if (source != null)
            {
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe.Id, RawJsonLd = source, CreatedDate = Imported });
                await db.SaveChangesAsync();
            }

            return recipe.Id;
        }

        private static readonly string[] Faithful =
        {
            "Heat the oven to 425 degrees F.",
            "Roast the potatoes on their tray for 25-30 minutes so they turn crisp and golden.",
        };

        private static readonly string[] CopiedStep =
        {
            "Heat the oven to 425 degrees F.",
            "Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy.",
        };

        private static readonly string[] LostTime =
        {
            "Heat the oven to 425 degrees F.",
            "Roast the potatoes on their tray for about 25 minutes so they turn crisp and golden.",
        };

        private static async Task<(long Passing, long Copied, long Lost, long Edited, long NoSource)> SeedSetAsync(ApplicationDbContext db)
        {
            var passing = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var copied = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            var lost = await SeedAsync(db, CurationStatusEnum.NonCurated, LostTime);
            var edited = await SeedAsync(db, CurationStatusEnum.Curated, LostTime, editedBy: Admin);
            var noSource = await SeedAsync(db, CurationStatusEnum.Curated, LostTime, source: null);
            await SeedAsync(db, CurationStatusEnum.NonCurated, CopiedStep, quarantined: true);
            await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep, license: RecipeLicenseStatus.PublicDomain);
            return (passing, copied, lost, edited, noSource);
        }

        [Fact]
        public async Task The_dry_run_reports_the_failures_by_status_and_writes_nothing()
        {
            await using var db = NewContext();
            var ids = await SeedSetAsync(db);

            var result = await new ProseRecheckService(db, new NoUser()).RecheckAsync();

            result.Applied.Should().BeFalse();
            result.Examined.Should().Be(5);
            result.Passed.Should().Be(1);
            result.Failed.Should().Be(2);
            result.FailedApproved.Should().Be(1);
            result.FailedApprovedCopied.Should().Be(1);
            result.FailedCopied.Should().Be(1);
            result.FailedLostNumber.Should().Be(1);
            result.FailedByStatus.Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<string, int> { ["Curated"] = 1, ["NonCurated"] = 1 });
            result.SkippedEditedByPerson.Should().Be(1);
            result.SkippedNoSource.Should().Be(1);
            result.Requarantined.Should().Be(0);
            result.Remaining.Should().Be(2);
            result.Samples.Select(s => s.RecipeId).Should().Equal(ids.Copied, ids.Lost);
            result.Samples[1].Detail.Should().Be("lost 30");

            (await db.Recipes.CountAsync(r => r.ContainsSourceProse)).Should().Be(1);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Apply_puts_the_failures_back_into_quarantine_and_the_undo_takes_them_out()
        {
            await using var db = NewContext();
            var ids = await SeedSetAsync(db);
            var service = new ProseRecheckService(db, new NoUser());
            var before = DateTime.UtcNow.AddSeconds(-1);

            var result = await service.RecheckAsync(apply: true);

            result.Requarantined.Should().Be(2);
            result.Remaining.Should().Be(0);
            var copied = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied);
            copied.ContainsSourceProse.Should().BeTrue();
            copied.ProseRewriteAttemptedAt.Should().BeNull();
            copied.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Passing)).ContainsSourceProse.Should().BeFalse();
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Edited)).ContainsSourceProse.Should().BeFalse();
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.RecheckChangeType).ToListAsync();
            journal.Select(a => a.EntityId).Should().BeEquivalentTo(new[] { ids.Copied, ids.Lost });
            journal.Should().OnlyContain(a => a.EntityType == "Recipe" && a.PropertyName == "ContainsSourceProse" && a.OldValue!.StartsWith("2026-11-04"));

            (await service.RecheckAsync()).Failed.Should().Be(0);

            var lost = await db.Recipes.SingleAsync(r => r.Id == ids.Lost);
            lost.ProseRewriteAttemptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var dryRun = await service.UndoAsync(before);
            db.ChangeTracker.Clear();

            dryRun.Should().BeEquivalentTo(new { Applied = false, Restored = 2, RestoredAfterFailedAttempt = 1, Skipped = 0 });
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied)).ContainsSourceProse.Should().BeTrue("a dry run writes nothing");

            var undo = await service.UndoAsync(before, apply: true);

            undo.Restored.Should().Be(2, "the lane's failed attempt changed none of the text");
            undo.Skipped.Should().Be(0);
            var restored = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied);
            restored.ContainsSourceProse.Should().BeFalse();
            restored.ProseRewriteAttemptedAt.Should().Be(Rewritten);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Lost)).ContainsSourceProse.Should().BeFalse();
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.RecheckChangeType)).Should().Be(0);
        }

        [Fact]
        public async Task The_undo_leaves_a_recipe_the_lane_has_cleared_with_a_rewrite_since()
        {
            await using var db = NewContext();
            var ids = await SeedSetAsync(db);
            var service = new ProseRecheckService(db, new NoUser());
            var before = DateTime.UtcNow.AddSeconds(-1);
            await service.RecheckAsync(apply: true);
            var lost = await db.Recipes.SingleAsync(r => r.Id == ids.Lost);
            lost.ContainsSourceProse = false;
            lost.ProseRewriteAttemptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undo = await service.UndoAsync(before, apply: true);

            undo.Should().BeEquivalentTo(new { Restored = 1, Skipped = 1, SkippedRewritten = 1 });
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.RecheckChangeType)).Should().Be(1);
        }

        [Fact]
        public async Task Apply_stops_at_the_batch_size()
        {
            await using var db = NewContext();
            await SeedSetAsync(db);

            var result = await new ProseRecheckService(db, new NoUser()).RecheckAsync(apply: true, maxRecipes: 1);

            result.Requarantined.Should().Be(1);
            result.Remaining.Should().Be(1);
        }

        [Fact]
        public async Task The_samples_and_counts_of_an_apply_are_the_recipes_it_put_back()
        {
            await using var db = NewContext();
            var lost = await SeedAsync(db, CurationStatusEnum.NonCurated, LostTime);
            await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);

            var result = await new ProseRecheckService(db, new NoUser()).RecheckAsync(apply: true, maxRecipes: 1);

            result.Requarantined.Should().Be(1);
            result.Samples.Select(s => s.RecipeId).Should().Equal(lost);
            result.RequarantinedByStatus.Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<string, int> { ["NonCurated"] = 1 });
        }
    }
}
