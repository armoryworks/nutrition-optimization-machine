using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Shown scraped recipes are checked against their stored source with prose check v2; the
    /// ones that fail have their prose withheld, approved first, with their curation status
    /// untouched, each journaled, and the undo releases them again unless their prose was
    /// replaced since. An apply needs a stock-phrase index, and an apply that leaves no failure
    /// shown is recorded as a full run for the weekly job.
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

        private static ProseRecheckService Service(ApplicationDbContext db) =>
            new(db, new NoUser(), new ProseStockService(db, new NoUser()));

        private static async Task BuildStockAsync(ApplicationDbContext db, int version = 1)
        {
            db.ProseStockPhrases.Add(new ProseStockPhraseEntity { Version = version, Hash = ProsePhrases.Hash("bring a large pot of"), SiteCount = 3 });
            await db.SaveChangesAsync();
        }

        private static async Task<long> SeedAsync(
            ApplicationDbContext db, CurationStatusEnum status, string[] steps, string? source = Source,
            bool quarantined = false, string license = RecipeLicenseStatus.Unknown, long? editedBy = null, bool withheld = false)
        {
            var recipe = new RecipeEntity
            {
                Name = "Roast potatoes",
                Description = "Weeknight potatoes, roasted until crisp.",
                ScrapedAtUtc = Imported,
                LicenseStatus = license,
                ContainsSourceProse = quarantined,
                ProseWithheld = withheld,
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
            "Roast the potatoes on the tray, placed in the oven, 25-30 minutes so they turn crisp and golden.",
        };

        private static readonly string[] CopiedStep =
        {
            "Heat the oven to 425 degrees F.",
            "Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy.",
        };

        private static readonly string[] CollapsedRange =
        {
            "Heat the oven to 425 degrees F.",
            "Roast the potatoes on the tray, placed in the oven, 25 minutes so they turn crisp and golden.",
        };

        private sealed record Seeded(long Passing, long Copied, long Collapsed, long Edited, long NoSource);

        private static async Task<Seeded> SeedSetAsync(ApplicationDbContext db)
        {
            var collapsed = await SeedAsync(db, CurationStatusEnum.NonCurated, CollapsedRange);
            var passing = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var copied = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            var edited = await SeedAsync(db, CurationStatusEnum.Curated, CollapsedRange, editedBy: Admin);
            var noSource = await SeedAsync(db, CurationStatusEnum.Curated, CollapsedRange, source: null);
            await SeedAsync(db, CurationStatusEnum.NonCurated, CopiedStep, quarantined: true);
            await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep, withheld: true);
            await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep, license: RecipeLicenseStatus.PublicDomain);
            return new Seeded(passing, copied, collapsed, edited, noSource);
        }

        [Fact]
        public async Task The_dry_run_reports_the_failures_by_reason_and_status_and_writes_nothing()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);

            var result = await Service(db).RecheckAsync();

            result.Applied.Should().BeFalse();
            result.CheckVersion.Should().Be(ProseCheck.Version);
            result.StockVersion.Should().Be(1);
            result.Examined.Should().Be(5, "quarantined, withheld and public-domain recipes are not shown copies");
            result.Passed.Should().Be(1);
            result.Failed.Should().Be(2);
            result.FailedApproved.Should().Be(1);
            result.FailedByReason.Should().ContainKey(ProseCheck.CopiedText).WhoseValue.Should().Be(1);
            result.FailedByReason.Should().ContainKey(ProseCheck.ChangedRange).WhoseValue.Should().Be(1);
            result.FailedApprovedByReason.Should().BeEquivalentTo(new Dictionary<string, int> { [ProseCheck.CopiedText] = 1 });
            result.FailedByStatus.Should().BeEquivalentTo(new Dictionary<string, int> { ["Curated"] = 1, ["NonCurated"] = 1 });
            result.SkippedEditedByPerson.Should().Be(1);
            result.SkippedEditedRecipeIds.Should().Equal(ids.Edited);
            result.SkippedNoSource.Should().Be(1);
            result.Withheld.Should().Be(0);
            result.Remaining.Should().Be(2);
            result.Samples.Select(s => s.RecipeId).Should().Equal(new[] { ids.Copied, ids.Collapsed }, "approved recipes come first");

            (await db.Recipes.CountAsync(r => r.ProseWithheld)).Should().Be(1);
            (await db.Recipes.CountAsync(r => r.ProseCheckVersion != null)).Should().Be(0);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task An_apply_is_refused_while_no_stock_phrase_index_has_been_built()
        {
            await using var db = NewContext();
            await SeedSetAsync(db);

            var result = await Service(db).RecheckAsync(apply: true);

            result.Applied.Should().BeFalse();
            result.Refused.Should().Contain("stock-phrase index");
            result.StockVersion.Should().Be(0);
            (await db.Recipes.CountAsync(r => r.ProseWithheld)).Should().Be(1);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Apply_withholds_the_failures_keeps_them_listed_and_records_the_run_and_the_undo_releases_them()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);

            var result = await service.RecheckAsync(apply: true);

            result.Withheld.Should().Be(2);
            result.Remaining.Should().Be(0);
            result.Stamped.Should().Be(1);
            result.RunRecorded.Should().BeTrue();
            result.WithheldByStatus.Should().BeEquivalentTo(new Dictionary<string, int> { ["Curated"] = 1, ["NonCurated"] = 1 });
            var copied = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied);
            copied.ProseWithheld.Should().BeTrue();
            copied.ContainsSourceProse.Should().BeFalse("a withheld recipe stays listed");
            copied.ProseCheckVersion.Should().Be(ProseCheck.Version);
            copied.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            copied.ProseRewriteAttemptedAt.Should().Be(Rewritten);
            copied.Description.Should().Be("Weeknight potatoes, roasted until crisp.", "the text stays in the database for the undo");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Passing)).ProseCheckVersion.Should().Be(ProseCheck.Version);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Edited)).ProseWithheld.Should().BeFalse();
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.WithholdChangeType).ToListAsync();
            journal.Select(a => a.EntityId).Should().BeEquivalentTo(new[] { ids.Copied, ids.Collapsed });
            journal.Should().OnlyContain(a => a.EntityType == "Recipe" && a.PropertyName == "ProseWithheld" && a.OldValue == string.Empty
                && a.NewValue!.StartsWith($"check {ProseCheck.Version} stock 1: "));
            var run = await service.LastRunAsync();
            run.Should().NotBeNull();
            run!.CheckVersion.Should().Be(ProseCheck.Version);
            run.StockVersion.Should().Be(1);
            (await service.DueAsync(TimeSpan.FromDays(7), DateTime.UtcNow)).Should().BeNull();

            (await service.RecheckAsync()).Failed.Should().Be(0, "withheld recipes are not checked again");

            var dryRun = await service.UndoAsync(before);
            db.ChangeTracker.Clear();

            dryRun.Should().BeEquivalentTo(new { Applied = false, Restored = 2, RestoredWithheld = 2, Skipped = 0, Remaining = 2 });
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied)).ProseWithheld.Should().BeTrue("a dry run writes nothing");

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.Should().BeEquivalentTo(new { Applied = true, Restored = 2, RestoredWithheld = 2, Skipped = 0, Remaining = 0 });
            var released = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied);
            released.ProseWithheld.Should().BeFalse();
            released.ProseCheckVersion.Should().BeNull("the check version it had before is restored");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType)).Should().Be(2, "the journal is kept");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.WithholdUndoChangeType)).Should().Be(2);

            (await service.UndoAsync(before)).Should().BeEquivalentTo(new { Restored = 0, Remaining = 0 }, "a repeat finds nothing left");
        }

        [Fact]
        public async Task A_recipe_whose_withhold_a_person_ended_is_left_alone_even_when_its_steps_carry_the_system_stamp()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var id = await SeedAsync(db, CurationStatusEnum.Curated, CollapsedRange);
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = id, ChangeType = ProseRecheckService.WithholdEndChangeType, PropertyName = "ProseWithheld",
                OldValue = "true", NewValue = "false", Timestamp = DateTime.UtcNow, ChangedByPersonId = Admin,
            });
            await db.SaveChangesAsync();

            var result = await Service(db).RecheckAsync(apply: true);

            result.SkippedEditedRecipeIds.Should().Equal(id);
            result.Withheld.Should().Be(0);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == id)).ProseWithheld.Should().BeFalse();
        }

        [Fact]
        public async Task The_undo_leaves_a_recipe_whose_prose_was_replaced_since()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);
            await service.RecheckAsync(apply: true);

            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = ids.Copied, ChangeType = ProseRecheckService.RewriteChangeType,
                PropertyName = "RecipeStep:1", OldValue = "x", NewValue = "[model] y", Timestamp = DateTime.UtcNow, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            var collapsed = await db.Recipes.SingleAsync(r => r.Id == ids.Collapsed);
            collapsed.ProseWithheld = false;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undo = await service.UndoAsync(before, apply: true);

            undo.Should().BeEquivalentTo(new { Restored = 0, Skipped = 2, SkippedRewritten = 2 });
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied)).ProseWithheld.Should().BeTrue("a rewrite journaled after the withhold is not undone by releasing it");
        }

        [Fact]
        public async Task Apply_stops_at_the_batch_size_approved_first_and_records_no_run_while_failures_remain()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);

            var result = await Service(db).RecheckAsync(apply: true, maxRecipes: 1);

            result.Withheld.Should().Be(1);
            result.Remaining.Should().Be(1);
            result.RunRecorded.Should().BeFalse();
            result.Samples.Select(s => s.RecipeId).Should().Equal(ids.Copied);
            (await Service(db).LastRunAsync()).Should().BeNull();
        }

        [Fact]
        public async Task A_hold_that_comes_on_during_an_apply_keeps_none_of_the_batch_it_would_save_and_stops()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            for (var i = 0; i < ProseRecheckService.WithholdBatch + 1; i++)
            {
                var recipe = new RecipeEntity
                {
                    Name = "Roast potatoes", Description = "Weeknight potatoes, roasted until crisp.", ScrapedAtUtc = Imported,
                    LicenseStatus = RecipeLicenseStatus.Unknown, CurationStatusId = (long)CurationStatusEnum.Curated,
                    RecipeSteps = CopiedStep.Select((text, n) => new RecipeStepEntity { StepNumber = n + 1, Description = text, LastModifiedByPersonId = SystemConstants.SystemPersonId }).ToList(),
                };
                db.Recipes.Add(recipe);
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity { Recipe = recipe, RawJsonLd = Source, CreatedDate = Imported });
            }

            var passing = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            db.ChangeTracker.Clear();
            var asked = 0;

            var result = await Service(db).RecheckAsync(apply: true, maxRecipes: 100000, hold: _ => Task.FromResult(++asked > 1));
            db.ChangeTracker.Clear();

            asked.Should().Be(2);
            result.Held.Should().BeTrue();
            result.Withheld.Should().Be(ProseRecheckService.WithholdBatch, "the first batch was saved before the hold came on");
            result.Remaining.Should().Be(1);
            result.RunRecorded.Should().BeFalse();
            result.Stamped.Should().Be(0);
            (await db.Recipes.CountAsync(r => r.ProseWithheld)).Should().Be(ProseRecheckService.WithholdBatch);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType)).Should().Be(ProseRecheckService.WithholdBatch);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == passing)).ProseCheckVersion.Should().BeNull();
            (await Service(db).LastRunAsync()).Should().BeNull();
        }

        [Fact]
        public async Task The_undo_still_takes_recipes_an_earlier_recheck_put_back_into_quarantine_out_of_it()
        {
            await using var db = NewContext();
            var id = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep, quarantined: true);
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = id, ChangeType = ProseRecheckService.RecheckChangeType, PropertyName = "ContainsSourceProse",
                OldValue = Rewritten.ToString("O"), NewValue = "lost 30", Timestamp = DateTime.UtcNow, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            var before = DateTime.UtcNow.AddMinutes(-1);

            var undo = await Service(db).UndoAsync(before, apply: true);

            undo.Should().BeEquivalentTo(new { Restored = 1, RestoredWithheld = 0, RestoredAfterFailedAttempt = 1 });
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == id)).ContainsSourceProse.Should().BeFalse();
        }

        [Fact]
        public void A_full_run_is_due_when_none_ran_when_a_version_changed_or_after_the_interval_and_never_without_a_stock_index()
        {
            var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            var week = TimeSpan.FromDays(7);
            var last = new ProseRecheckRun(now.AddDays(-1), ProseCheck.Version, 3);

            ProseRecheckService.Due(null, ProseCheck.Version, 0, week, now).Should().BeNull();
            ProseRecheckService.Due(null, ProseCheck.Version, 3, week, now).Should().Be("no full run recorded");
            ProseRecheckService.Due(last, ProseCheck.Version, 3, week, now).Should().BeNull();
            ProseRecheckService.Due(last, ProseCheck.Version + 1, 3, week, now).Should().StartWith("prose check version");
            ProseRecheckService.Due(last, ProseCheck.Version, 4, week, now).Should().StartWith("stock index version");
            ProseRecheckService.Due(last with { At = now.AddDays(-7) }, ProseCheck.Version, 3, week, now).Should().StartWith("last full run");
        }

        [Fact]
        public async Task The_staged_import_record_stored_after_the_json_ld_is_not_taken_for_the_source()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var copied = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            var record = Nom.Orch.Services.Support.SourceRecord.Content(new Nom.Orch.UtilityInterfaces.ScraperRecipe
            {
                Name = "Roast potatoes",
                SourceUrl = "https://example.com/roast-potatoes",
                Steps = { new() { Order = 1, Instruction = "Roast." } },
            });
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = copied, RawJsonLd = record, Format = ScrapedDocumentFormat.ScraperRecipe, CreatedDate = Imported });
            await db.SaveChangesAsync();

            var result = await Service(db).RecheckAsync();

            result.SkippedNoSource.Should().Be(0);
            result.FailedByReason.Should().ContainKey(ProseCheck.CopiedText).WhoseValue.Should().Be(1);
        }
    }
}
