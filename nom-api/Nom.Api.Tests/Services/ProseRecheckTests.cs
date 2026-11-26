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
    /// Every shown scraped recipe whose method is an earlier paraphrase of its stored source (no clear
    /// licence, no person's edit, not written by the reword lane) has its prose withheld, approved first
    /// and then the most copied, with its curation status untouched, each journaled with the check's
    /// verdict and its queue figure, and the undo releases them again unless their prose was replaced
    /// since. An apply needs a stock-phrase index, and an apply that leaves no earlier paraphrase shown
    /// is recorded as a full run for the weekly job.
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

        private static readonly string[] VerbSaidAnotherWay =
        {
            "Heat the oven to 425 degrees F.",
            "Set the potatoes on the tray in the oven for 25-30 minutes so they turn crisp and golden.",
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

        private static async Task JournalLaneAsync(ApplicationDbContext db, long recipeId, string laneVersion, bool undone = false)
        {
            var at = DateTime.UtcNow;
            var state = new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = recipeId, ChangeType = ProseRecheckService.RewriteChangeType,
                PropertyName = ProseLaneJournal.StateProperty, OldValue = "{}", NewValue = $"[qwen2.5:14b-instruct {laneVersion}] {{}}", Timestamp = at, ChangedByPersonId = SystemConstants.SystemPersonId,
            };
            db.AuditLogEntries.Add(state);
            await db.SaveChangesAsync();
            if (undone)
            {
                db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = "Recipe", EntityId = recipeId, ChangeType = ProseLaneJournal.UndoChangeType, PropertyName = ProseLaneJournal.StateProperty,
                    OldValue = state.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), NewValue = "{}", Timestamp = at.AddSeconds(1), ChangedByPersonId = SystemConstants.SystemPersonId,
                });
                await db.SaveChangesAsync();
            }

            db.ChangeTracker.Clear();
        }

        [Fact]
        public async Task The_dry_run_counts_every_shown_earlier_paraphrase_and_the_exempt_recipes_by_why_and_writes_nothing()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);

            var result = await Service(db).RecheckAsync();

            result.Applied.Should().BeFalse();
            result.CheckVersion.Should().Be(ProseCheck.Version);
            result.StockVersion.Should().Be(1);
            result.Examined.Should().Be(5, "quarantined, withheld and public-domain recipes are not shown earlier paraphrases");
            result.EarlierParaphrases.Should().Be(3, "the recipe the check reads no copy in is an earlier paraphrase all the same");
            result.EarlierParaphrasesApproved.Should().Be(2);
            result.ExemptClearLicence.Should().Be(1);
            result.SkippedEditedByPerson.Should().Be(1);
            result.SkippedEditedRecipeIds.Should().Equal(ids.Edited);
            result.SkippedNoSource.Should().Be(1);
            result.ExemptLaneWritten.Should().Be(0);
            result.ExaminedWithheld.Should().Be(1);
            result.ByCopy.Values.Sum().Should().Be(4, "the shown earlier paraphrases and the one already withheld");
            result.ByCopy.Should().ContainKey(ProseRecheckService.Mostly);
            result.Withheld.Should().Be(0);
            result.Remaining.Should().Be(3);
            result.Samples.Select(s => s.RecipeId).Should().Equal(new[] { ids.Copied, ids.Passing, ids.Collapsed }, "approved recipes come first, the most copied first among them");
            result.Samples[0].Copied.Should().Be(ProseRecheckService.Mostly);

            (await db.Recipes.CountAsync(r => r.ProseWithheld)).Should().Be(1);
            (await db.Recipes.CountAsync(r => r.ProseCopiedSentences != 0)).Should().Be(0);
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
        public async Task Apply_withholds_every_earlier_paraphrase_whatever_the_check_reads_sets_the_queue_figures_records_the_run_and_the_undo_releases_them()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);

            var result = await service.RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            result.Withheld.Should().Be(3);
            result.Remaining.Should().Be(0);
            result.RunRecorded.Should().BeTrue();
            result.WithheldByStatus.Should().BeEquivalentTo(new Dictionary<string, int> { ["Curated"] = 2, ["NonCurated"] = 1 });
            var passing = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Passing);
            passing.ProseWithheld.Should().BeTrue("no word-order rule can tell a light edit from a rebuilt sentence, so the old text is withheld by its origin");
            passing.ContainsSourceProse.Should().BeFalse("a withheld recipe stays listed");
            passing.ProseCheckVersion.Should().Be(ProseCheck.Version);
            passing.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            passing.Description.Should().Be("Weeknight potatoes, roasted until crisp.", "the text stays in the database for the undo");
            var copied = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied);
            copied.ProseWithheld.Should().BeTrue();
            copied.ProseCopiedSentences.Should().BeGreaterThan(passing.ProseCopiedSentences, "the queue figure counts the sentences read as copied or kept");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Edited)).ProseWithheld.Should().BeFalse();
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.NoSource)).ProseWithheld.Should().BeFalse();
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.WithholdChangeType).ToListAsync();
            journal.Select(a => a.EntityId).Should().BeEquivalentTo(new[] { ids.Copied, ids.Passing, ids.Collapsed });
            journal.Should().OnlyContain(a => a.EntityType == "Recipe" && a.PropertyName == "ProseWithheld" && a.OldValue == string.Empty
                && a.NewValue!.StartsWith($"check {ProseCheck.Version} stock 1: earlier paraphrase, withheld until reworded as a whole: "));
            var figures = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.RepairChangeType).ToListAsync();
            figures.Should().HaveCount(4, "the three withheld now and the one already withheld get their queue figure");
            figures.Should().OnlyContain(a => a.OldValue == "0" && a.NewValue!.Contains("queue figure: "));
            var already = await db.Recipes.AsNoTracking().SingleAsync(r => r.ProseWithheld && r.Id != ids.Copied && r.Id != ids.Passing && r.Id != ids.Collapsed && !r.ContainsSourceProse);
            already.ProseCopiedSentences.Should().BeGreaterThan(0);
            var run = await service.LastRunAsync();
            run.Should().NotBeNull();
            run!.CheckVersion.Should().Be(ProseCheck.Version);
            (await service.DueAsync(TimeSpan.FromDays(7), DateTime.UtcNow)).Should().BeNull();

            var again = await service.RecheckAsync();
            again.EarlierParaphrases.Should().Be(0, "withheld recipes are not withheld again");
            again.ExaminedWithheld.Should().Be(4);
            again.QueueFigures.Should().Be(0, "every figure is already set");

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.Should().BeEquivalentTo(new { Applied = true, Restored = 3, RestoredWithheld = 3, RestoredRepair = 4, Skipped = 0, Remaining = 0 }, o => o.ExcludingMissingMembers());
            var released = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied);
            released.ProseWithheld.Should().BeFalse();
            released.ProseCheckVersion.Should().BeNull("the check version it had before is restored");
            released.ProseCopiedSentences.Should().Be(0, "the undo puts the figure back");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == already.Id)).Should().Match<RecipeEntity>(r => r.ProseWithheld && r.ProseCopiedSentences == 0);
            (await service.UndoAsync(before)).Should().BeEquivalentTo(new { Restored = 0, Remaining = 0 }, o => o.ExcludingMissingMembers());
        }

        [Fact]
        public async Task A_method_the_reword_lane_wrote_as_a_whole_is_exempt_and_one_an_earlier_lane_wrote_or_whose_rewrite_was_undone_is_withheld()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var whole = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var earlier = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var undone = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            await JournalLaneAsync(db, whole, ProseLanePrompts.LaneVersionPrefix + "0123456789ab");
            await JournalLaneAsync(db, earlier, "v3-0123456789ab");
            await JournalLaneAsync(db, undone, ProseLanePrompts.LaneVersionPrefix + "0123456789ab", undone: true);

            var result = await Service(db).RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            result.ExemptLaneWritten.Should().Be(1);
            result.Withheld.Should().Be(2);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == whole)).ProseWithheld.Should().BeFalse();
            (await db.Recipes.AsNoTracking().Where(r => r.Id == earlier || r.Id == undone).ToListAsync()).Should().OnlyContain(r => r.ProseWithheld);
        }

        [Fact]
        public void A_journaled_value_names_a_whole_method_lane_version_only_in_its_tag()
        {
            ProseLanePrompts.IsWholeMethodTag("[qwen2.5:14b-instruct v4-0123456789ab] {}").Should().BeTrue();
            ProseLanePrompts.IsWholeMethodTag("[rule-only v4-0123456789ab] {}").Should().BeTrue();
            ProseLanePrompts.IsWholeMethodTag("[qwen2.5:14b-instruct v3-0123456789ab] {}").Should().BeFalse();
            ProseLanePrompts.IsWholeMethodTag("{} [model v4-x]").Should().BeFalse();
            ProseLanePrompts.IsWholeMethodTag(null).Should().BeFalse();
            new ProseLaneModels("qwen2.5:14b-instruct", "mistral-nemo", "phi4").Version.Should().StartWith(ProseLanePrompts.LaneVersionPrefix);
        }

        [Fact]
        public async Task Apply_stops_at_the_batch_size_approved_and_most_copied_first_and_records_no_run_while_paraphrases_remain()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);

            var result = await Service(db).RecheckAsync(apply: true, maxRecipes: 1);

            result.Withheld.Should().Be(1);
            result.Remaining.Should().Be(2);
            result.RunRecorded.Should().BeFalse();
            result.Samples.Select(s => s.RecipeId).Should().Equal(ids.Copied);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.RepairChangeType)).Should().Be(1, "only the recipe withheld in this batch gets its figure; those already withheld wait for a full apply");
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
            result.Remaining.Should().Be(2);
            result.RunRecorded.Should().BeFalse();
            (await db.Recipes.CountAsync(r => r.ProseWithheld)).Should().Be(ProseRecheckService.WithholdBatch);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType)).Should().Be(ProseRecheckService.WithholdBatch);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == passing)).ProseWithheld.Should().BeFalse();
            (await Service(db).LastRunAsync()).Should().BeNull();
        }

        private static async Task MarkUnsureAsync(ApplicationDbContext db, params long[] ids)
        {
            foreach (var recipe in await db.Recipes.Where(r => ids.Contains(r.Id)).ToListAsync())
            {
                recipe.ProseUnsure = true;
                recipe.ProseCheckVersion = ProseCheck.Version - 1;
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        [Fact]
        public async Task A_recipe_marked_unsure_loses_the_mark_when_it_is_withheld_and_the_undo_restores_it()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var marked = await SeedAsync(db, CurationStatusEnum.Curated, VerbSaidAnotherWay);
            await MarkUnsureAsync(db, marked);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);

            var result = await service.RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            result.Withheld.Should().Be(1);
            result.ClearedUnsure.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == marked)).Should().Match<RecipeEntity>(r => r.ProseWithheld && !r.ProseUnsure);
            (await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == ProseRecheckService.UnsureChangeType)).NewValue
                .Should().StartWith($"false | check {ProseCheck.Version} stock 1: withheld as an earlier paraphrase");

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.RestoredUnsure.Should().Be(1);
            undo.RestoredWithheld.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == marked)).Should().Match<RecipeEntity>(r => r.ProseUnsure && !r.ProseWithheld);
        }

        [Fact]
        public async Task A_mark_cleared_because_a_person_edited_the_recipe_is_journaled_as_not_checked()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var edited = await SeedAsync(db, CurationStatusEnum.Curated, VerbSaidAnotherWay, editedBy: Admin);
            var ended = await SeedAsync(db, CurationStatusEnum.Curated, VerbSaidAnotherWay);
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = ended, ChangeType = ProseRecheckService.WithholdEndChangeType, PropertyName = "ProseWithheld",
                OldValue = "true", NewValue = "false", Timestamp = DateTime.UtcNow, ChangedByPersonId = Admin,
            });
            await db.SaveChangesAsync();
            await MarkUnsureAsync(db, edited, ended);

            var result = await Service(db).RecheckAsync(apply: true);

            result.ClearedUnsure.Should().Be(2);
            result.Withheld.Should().Be(0);
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.UnsureChangeType).ToDictionaryAsync(a => a.EntityId, a => a.NewValue);
            journal[edited].Should().Be($"false | check {ProseCheck.Version} stock 1: {ProseRecheckService.NotCheckedEdited}");
            journal[ended].Should().Be($"false | check {ProseCheck.Version} stock 1: {ProseRecheckService.NotCheckedWithholdEnded}");
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

            undo.Should().BeEquivalentTo(new { Restored = 1, Skipped = 2, SkippedRewritten = 2 }, o => o.ExcludingMissingMembers());
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied)).ProseWithheld.Should().BeTrue("a rewrite journaled after the withhold is not undone by releasing it");
        }

        [Fact]
        public async Task The_undo_releases_a_recipe_whose_lane_rewrite_after_the_withhold_was_itself_undone()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);
            await service.RecheckAsync(apply: true);

            var at = DateTime.UtcNow;
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = ids.Copied, ChangeType = ProseRecheckService.RewriteChangeType,
                PropertyName = "RecipeStep:1", OldValue = "x", NewValue = "[model v4-x] y", Timestamp = at, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            await JournalLaneAsync(db, ids.Copied, "v4-x", undone: true);
            var state = await db.AuditLogEntries.SingleAsync(a => a.PropertyName == ProseLaneJournal.StateProperty && a.ChangeType == ProseRecheckService.RewriteChangeType);
            foreach (var step in await db.AuditLogEntries.Where(a => a.PropertyName == "RecipeStep:1").ToListAsync()) step.Timestamp = state.Timestamp;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undo = await service.UndoAsync(before);

            undo.SkippedRewritten.Should().Be(0, "the lane's own undo took its rewrite back, so no rewrite is in force");
            undo.RestoredWithheld.Should().Be(3);
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

            undo.Should().BeEquivalentTo(new { Restored = 1, RestoredWithheld = 0, RestoredAfterFailedAttempt = 1 }, o => o.ExcludingMissingMembers());
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
            ProseRecheckService.Due(last with { CheckVersion = 7 }, ProseCheck.Version, 3, week, now).Should().StartWith("prose check version 7", "a run under check 7 sorted the earlier paraphrases by the check; check 8 withholds them by origin");
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
            result.EarlierParaphrases.Should().Be(1);
            result.ByCopy.Should().BeEquivalentTo(new Dictionary<string, int> { [ProseRecheckService.Mostly] = 1 });
        }

        private const string SourceWithSizes = """
            {"@type":"Recipe","description":"Crisp roast potatoes for a weeknight.",
             "recipeIngredient":[
               "4 pieces Reynolds Wrap® Heavy Duty Aluminum Foil (12 x 18 inches)",
               "4 (4-inch) corn tortillas*, sliced into 1/4-inch x 2-inch strips",
               "1 pound potatoes, cut into ~1-inch x 1-inch cubes",
               "2 tablespoons butter, cut into 2 x 1/4-inch slices"],
             "recipeInstructions":[
               {"@type":"HowToStep","text":"Preheat the oven to 425 degrees F."},
               {"@type":"HowToStep","text":"Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy."}]}
            """;

        [Fact]
        public async Task A_recipe_whose_ingredient_lines_state_a_size_of_two_lengths_is_checked_like_any_other()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var passing = await SeedAsync(db, CurationStatusEnum.Curated, Faithful, source: SourceWithSizes);
            var copied = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep, source: SourceWithSizes);

            var result = await Service(db).RecheckAsync();

            result.Errored.Should().Be(0);
            result.EarlierParaphrases.Should().Be(2);
            result.Samples.Select(s => s.RecipeId).Should().Equal(copied, passing);
        }

        [Fact]
        public async Task A_recipe_the_check_fails_on_is_counted_named_and_withheld_as_a_check_error_and_the_run_goes_on_to_record()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var broken = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var copied = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            var service = Service(db);
            service.BeforeCheck = id =>
            {
                if (id == broken) throw new ArgumentOutOfRangeException("startIndex", "startIndex cannot be larger than length of string.");
            };

            var result = await service.RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            result.Errored.Should().Be(1);
            result.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new { RecipeId = broken });
            result.Errors[0].Error.Should().StartWith("ArgumentOutOfRangeException: startIndex cannot be larger");
            result.ByCopy.Should().ContainKey(ProseRecheckService.CheckErrorReason).WhoseValue.Should().Be(1);
            result.Withheld.Should().Be(2);
            result.Remaining.Should().Be(0);
            result.RunRecorded.Should().BeTrue();
            (await db.Recipes.Where(r => r.ProseWithheld).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync()).Should().Equal(new[] { broken, copied }.OrderBy(i => i));
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType && a.EntityId == broken))
                .NewValue.Should().Contain("check-error: ArgumentOutOfRangeException");
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == ProseRecheckService.RunChangeType)).NewValue.Should().EndWith("errored 1");
        }
    }
}
