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
    /// Shown scraped recipes are checked against their stored source with the prose check; the
    /// ones that fail have their prose withheld, approved first, with their curation status
    /// untouched, each journaled, and the undo releases them again unless their prose was
    /// replaced since. Recipes the check is only unsure of stay shown, counted and marked. An
    /// apply needs a stock-phrase index, and an apply that leaves no failure shown is recorded as
    /// a full run for the weekly job.
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

        [Fact]
        public async Task The_dry_run_reports_the_failures_by_reason_and_status_and_writes_nothing()
        {
            using var proven = ProseCheck.AssumeProven(ProseCheck.ChangedRange);
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
        public async Task A_fact_class_the_committed_record_does_not_prove_only_marks_its_recipe_unsure()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);
            ProseCheck.ProvenClasses.Should().NotContain(ProseCheck.ChangedRange, "the committed per-class record proves no fact class on too few judged rows");

            var result = await Service(db).RecheckAsync(apply: true);

            result.Failed.Should().Be(1);
            result.FailedByReason.Should().BeEquivalentTo(new Dictionary<string, int> { [ProseCheck.CopiedText] = 1 });
            result.UnsureByReason.Should().ContainKey(ProseCheck.ChangedRange);
            var collapsed = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Collapsed);
            collapsed.ProseWithheld.Should().BeFalse("a fact class below the bar writes nothing");
            collapsed.ProseUnsure.Should().BeTrue("it is marked for the prose judge and counted");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == ids.Copied)).ProseWithheld.Should().BeTrue("the copy rule keeps its own bar and fails the recipe");
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
            using var proven = ProseCheck.AssumeProven(ProseCheck.ChangedRange);
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

        private const string BarleySource = """
            {"@type":"Recipe","description":"Pearl barley with roast beetroot, dill and feta.",
             "recipeInstructions":[
               {"@type":"HowToStep","text":"Rinse the pearl barley under cold water and tip it into a deep saucepan with the bay leaves. Cover it well with cold water, bring it up to a lively boil, then turn the heat right down. Let it cook gently without a lid for 35 to 40 minutes, until the grains are plump but still chewy. Drain it well and spread the barley on a tray to steam dry."},
               {"@type":"HowToStep","text":"While the barley cooks, scrub the beetroot and cut it into thin wedges. Toss the wedges with the oil, the caraway seeds and a good pinch of salt. Roast them at 200°C for 25 minutes, turning them once halfway. Fold the warm beetroot through the barley with the dill and the crumbled feta."}]}
            """;

        private static readonly string[] LightEdit =
        {
            "Wash the pearl barley in cold water, then put it in a deep pan along with the bay leaves. Pour over plenty of cold water, bring to a lively boil and lower the heat. Simmer it gently, lid off, for 35 to 40 minutes, until the grains are plump yet still chewy. Drain and spread the barley over a tray so it steams dry.",
            "As the barley cooks, scrub the beetroot and slice it into thin wedges. Toss these with oil, caraway seeds and a good pinch of salt. Roast at 200°C for 25 minutes, turning once halfway through. Fold the warm beetroot into the barley along with dill and crumbled feta.",
        };

        private const string ChickenSource = """
            {"@type":"Recipe","description":"Paprika chicken thighs with lemon.",
             "recipeInstructions":[
               {"@type":"HowToStep","text":"Heat the oven to 190°C. Rub the chicken thighs all over with the paprika, garlic and a little salt, then lay them skin side up in a roasting tin."},
               {"@type":"HowToStep","text":"Roast for 40 minutes, until the skin is crisp and the juices run clear. Rest the thighs for five minutes under a loose sheet of foil before serving them with the lemon wedges."}]}
            """;

        private static readonly string[] SomeSentencesKept =
        {
            "Turn the oven on to 190°C. Coat the thighs in paprika, garlic and salt, and arrange them in a roasting tin with their skin facing up.",
            "Give them 40 minutes in the oven, until crisp and cooked through. Rest the thighs for five minutes under a loose sheet of foil before serving them with lemon wedges.",
        };

        [Fact]
        public async Task The_report_gives_the_recipes_withheld_with_some_copied_sentences_withheld_until_reworded_and_with_them_shown_while_reworded()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource);
            await SeedAsync(db, CurationStatusEnum.Curated, SomeSentencesKept, ChickenSource);
            await SeedAsync(db, CurationStatusEnum.NonCurated, SomeSentencesKept, ChickenSource);

            var dry = await Service(db).RecheckAsync();

            dry.WithheldIfRepairWithheld.Should().Be(3, "as built, a recipe with some copied sentences is withheld until reworded");
            dry.WithheldIfRepairShown.Should().Be(1, "shown while reworded, only the recipe mostly copied is withheld");
            dry.WithheldIfRepairWithheldApproved.Should().Be(2);
            dry.WithheldIfRepairShownApproved.Should().Be(1);
            dry.Withheld.Should().Be(0, "the figures are a report and change nothing");
        }

        [Fact]
        public async Task The_two_withhold_figures_count_the_recipes_already_withheld_and_change_none_of_them()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource);
            await SeedAsync(db, CurationStatusEnum.Curated, SomeSentencesKept, ChickenSource);
            await SeedAsync(db, CurationStatusEnum.NonCurated, SomeSentencesKept, ChickenSource);
            await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource, withheld: true);
            await SeedAsync(db, CurationStatusEnum.NonCurated, LightEdit, BarleySource, withheld: true);
            await SeedAsync(db, CurationStatusEnum.Curated, SomeSentencesKept, ChickenSource, withheld: true);
            await SeedAsync(db, CurationStatusEnum.Curated, Faithful, withheld: true);
            await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource, withheld: true, editedBy: Admin);
            await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource, withheld: true, quarantined: true);

            var dry = await Service(db).RecheckAsync();

            dry.Examined.Should().Be(3);
            dry.ExaminedWithheld.Should().Be(4, "the recipe a person edited and the quarantined one are left out");
            dry.WithheldIfRepairWithheld.Should().Be(6);
            dry.WithheldIfRepairShown.Should().Be(3);
            dry.WithheldIfRepairWithheldApproved.Should().Be(4);
            dry.WithheldIfRepairShownApproved.Should().Be(2);
            dry.AlreadyWithheldIfRepairWithheld.Should().Be(3);
            dry.AlreadyWithheldIfRepairShown.Should().Be(2);

            var applied = await Service(db).RecheckAsync(apply: true);

            applied.Withheld.Should().Be(3);
            applied.WithheldIfRepairWithheld.Should().Be(6, "after an apply the recipes it withheld still count");
            applied.WithheldIfRepairShown.Should().Be(3);
            applied.AlreadyWithheldIfRepairWithheld.Should().Be(3, "the figures are taken before the apply withholds anything");
            var again = await Service(db).RecheckAsync();
            again.Examined.Should().Be(0);
            again.WithheldIfRepairWithheld.Should().Be(6);
            again.WithheldIfRepairShown.Should().Be(3);
            again.AlreadyWithheldIfRepairWithheld.Should().Be(6);
            (await db.Recipes.CountAsync(r => r.ProseWithheld)).Should().Be(9);
        }

        [Fact]
        public async Task A_recipe_mostly_lightly_edited_is_withheld_and_one_with_some_copied_sentences_is_withheld_with_its_count_until_reworded_and_both_are_restored_by_the_undo()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var mostly = await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource);
            var some = await SeedAsync(db, CurationStatusEnum.Curated, SomeSentencesKept, ChickenSource);
            var service = Service(db);

            var dry = await service.RecheckAsync();

            dry.Failed.Should().Be(1);
            dry.FailedByReason.Should().ContainKey(ProseCheck.CopiedText);
            dry.Repair.Should().Be(1);
            dry.RepairApproved.Should().Be(1);
            dry.RepairSentences.Should().Be(1);
            dry.MarkedRepair.Should().Be(0, "a dry run marks nothing");
            dry.Remaining.Should().Be(2, "a recipe with copied sentences is not shown either");

            var before = DateTime.UtcNow.AddSeconds(-1);
            var applied = await service.RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            applied.Withheld.Should().Be(2);
            applied.WithheldRepair.Should().Be(1);
            applied.MarkedRepair.Should().Be(1);
            applied.Remaining.Should().Be(0);
            applied.RunRecorded.Should().BeTrue();
            var withheld = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == mostly);
            withheld.ProseWithheld.Should().BeTrue();
            withheld.ProseCopiedSentences.Should().Be(0, "a recipe withheld as a whole has no work list of its own");
            var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType && a.EntityId == mostly);
            entry.NewValue.Should().StartWith($"check {ProseCheck.Version} stock 1: failed: ").And.Contain("sentences copied");
            var repair = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == some);
            repair.ProseWithheld.Should().BeTrue("a recipe is shown only when all its sentences pass");
            repair.ProseCopiedSentences.Should().Be(1, "its copied sentences are the lane's work list");
            (await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType && a.EntityId == some))
                .NewValue.Should().StartWith($"check {ProseCheck.Version} stock 1: repair: ");
            (await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == ProseRecheckService.RepairChangeType && a.EntityId == some))
                .NewValue.Should().StartWith("1 | check ").And.Contain("step 2 sentence 2");

            var again = await service.RecheckAsync(apply: true);
            again.Examined.Should().Be(0, "neither recipe is shown");

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.RestoredWithheld.Should().Be(2);
            undo.RestoredRepair.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == mostly)).ProseWithheld.Should().BeFalse();
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == some)).Should().Match<RecipeEntity>(r => !r.ProseWithheld && r.ProseCopiedSentences == 0);
        }

        [Fact]
        public async Task The_batch_limit_holds_recipes_with_copied_sentences_too_and_marks_none_beyond_it()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var mostly = await SeedAsync(db, CurationStatusEnum.Curated, LightEdit, BarleySource);
            var first = await SeedAsync(db, CurationStatusEnum.Curated, SomeSentencesKept, ChickenSource);
            var second = await SeedAsync(db, CurationStatusEnum.Curated, SomeSentencesKept, ChickenSource);
            var service = Service(db);

            var before = DateTime.UtcNow.AddSeconds(-1);
            var applied = await service.RecheckAsync(apply: true, maxRecipes: 2);
            db.ChangeTracker.Clear();

            applied.Withheld.Should().Be(2);
            applied.Remaining.Should().Be(1);
            applied.RunRecorded.Should().BeFalse();
            var recipes = await db.Recipes.AsNoTracking().Where(r => r.Id == mostly || r.Id == first || r.Id == second).ToListAsync();
            recipes.Count(r => r.ProseWithheld).Should().Be(2);
            recipes.Single(r => !r.ProseWithheld).ProseCopiedSentences.Should().Be(0, "a recipe beyond the batch is neither withheld nor marked");
            (await db.AuditLogEntries.AsNoTracking().CountAsync(a => a.ChangeType == ProseRecheckService.RepairChangeType)).Should().Be(applied.MarkedRepair);

            var undo = await service.UndoAsync(before);
            (undo.RestoredWithheld + undo.RestoredRepair).Should().BeLessOrEqualTo(4);
            undo.RestoredWithheld.Should().Be(2);
        }

        [Fact]
        public async Task A_recipe_the_check_is_unsure_of_stays_shown_and_is_counted_marked_cleared_and_restored_by_the_undo()
        {
            ProseCheck.Check(RewriteFidelity.Source(Source)!, new RewriteFidelity.SourceText(string.Empty, VerbSaidAnotherWay)).Outcome.Should().Be("unsure");
            await using var db = NewContext();
            await BuildStockAsync(db);
            var unsure = await SeedAsync(db, CurationStatusEnum.Curated, VerbSaidAnotherWay);
            var passing = await SeedAsync(db, CurationStatusEnum.NonCurated, Faithful);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);

            var dry = await service.RecheckAsync();

            dry.Should().BeEquivalentTo(new { Failed = 0, Passed = 1, Unsure = 1, UnsureApproved = 1, MarkedUnsure = 0 }, o => o.ExcludingMissingMembers());
            dry.UnsureByReason.Should().ContainKey(ProseCheck.LostStep);
            dry.UnsureSamples.Select(s => s.RecipeId).Should().Equal(unsure);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == unsure)).ProseUnsure.Should().BeFalse("a dry run writes nothing");

            var applied = await service.RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            applied.Withheld.Should().Be(0, "an unsure recipe is never withheld");
            applied.MarkedUnsure.Should().Be(1);
            applied.RunRecorded.Should().BeTrue();
            var marked = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == unsure);
            marked.ProseUnsure.Should().BeTrue();
            marked.ProseWithheld.Should().BeFalse();
            marked.ProseCheckVersion.Should().Be(ProseCheck.Version);
            (await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == ProseRecheckService.UnsureChangeType)).NewValue
                .Should().StartWith($"true | check {ProseCheck.Version} stock 1: unsure: lost-step");
            (await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == ProseRecheckService.RunChangeType)).NewValue.Should().Contain("unsure 1");
            (await service.RecheckAsync(apply: true)).MarkedUnsure.Should().Be(0, "a mark already set is not set again");

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.RestoredUnsure.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == unsure)).ProseUnsure.Should().BeFalse("the undo puts the mark back as it was");

            await service.RecheckAsync(apply: true);
            foreach (var step in await db.RecipeSteps.Where(s => s.RecipeId == unsure).ToListAsync())
                step.Description = Faithful[step.StepNumber - 1];
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var cleared = await service.RecheckAsync(apply: true);
            db.ChangeTracker.Clear();

            cleared.ClearedUnsure.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == unsure)).ProseUnsure.Should().BeFalse();
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == passing)).ProseUnsure.Should().BeFalse();
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
        public async Task A_recipe_marked_unsure_that_now_fails_loses_the_mark_whether_or_not_this_run_withholds_it_and_the_undo_restores_it()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var first = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            var beyond = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            await MarkUnsureAsync(db, first, beyond);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);

            var result = await service.RecheckAsync(apply: true, maxRecipes: 1);
            db.ChangeTracker.Clear();

            result.Withheld.Should().Be(1);
            result.Remaining.Should().Be(1);
            result.ClearedUnsure.Should().Be(2);
            var withheld = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == first);
            withheld.ProseWithheld.Should().BeTrue();
            withheld.ProseUnsure.Should().BeFalse("a withheld recipe carries no unsure mark back when the lane releases it");
            var shown = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == beyond);
            shown.ProseWithheld.Should().BeFalse();
            shown.ProseUnsure.Should().BeFalse("a recipe that fails is not one the check is unsure of");
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.UnsureChangeType).ToDictionaryAsync(a => a.EntityId, a => a.NewValue);
            journal[first].Should().StartWith($"false | check {ProseCheck.Version} stock 1: failed and withheld: copied-text");
            journal[beyond].Should().StartWith($"false | check {ProseCheck.Version} stock 1: failed, not withheld in this run: copied-text");

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.RestoredUnsure.Should().Be(2);
            undo.RestoredWithheld.Should().Be(1);
            (await db.Recipes.AsNoTracking().Where(r => r.Id == first || r.Id == beyond).ToListAsync())
                .Should().OnlyContain(r => r.ProseUnsure && !r.ProseWithheld);
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
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == ProseRecheckService.UnsureChangeType).ToDictionaryAsync(a => a.EntityId, a => a.NewValue);
            journal[edited].Should().Be($"false | check {ProseCheck.Version} stock 1: {ProseRecheckService.NotCheckedEdited}");
            journal[ended].Should().Be($"false | check {ProseCheck.Version} stock 1: {ProseRecheckService.NotCheckedWithholdEnded}");
            journal.Values.Should().NotContain(v => v!.Contains("passed"));
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
            using var proven = ProseCheck.AssumeProven(ProseCheck.ChangedRange);
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
        public async Task The_undo_releases_a_recipe_whose_lane_rewrite_after_the_withhold_was_itself_undone()
        {
            using var proven = ProseCheck.AssumeProven(ProseCheck.ChangedRange);
            await using var db = NewContext();
            await BuildStockAsync(db);
            var ids = await SeedSetAsync(db);
            var service = Service(db);
            var before = DateTime.UtcNow.AddSeconds(-1);
            await service.RecheckAsync(apply: true);

            var at = DateTime.UtcNow;
            db.AuditLogEntries.AddRange(
                new AuditLogEntryEntity
                {
                    EntityType = "Recipe", EntityId = ids.Copied, ChangeType = ProseRecheckService.RewriteChangeType,
                    PropertyName = "RecipeStep:1", OldValue = "x", NewValue = "[model v2-x] y", Timestamp = at, ChangedByPersonId = SystemConstants.SystemPersonId,
                },
                new AuditLogEntryEntity
                {
                    EntityType = "Recipe", EntityId = ids.Copied, ChangeType = ProseRecheckService.RewriteChangeType,
                    PropertyName = ProseLaneJournal.StateProperty, OldValue = "{}", NewValue = "[model v2-x] {}", Timestamp = at, ChangedByPersonId = SystemConstants.SystemPersonId,
                });
            await db.SaveChangesAsync();
            var state = await db.AuditLogEntries.SingleAsync(a => a.PropertyName == ProseLaneJournal.StateProperty);
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe", EntityId = ids.Copied, ChangeType = ProseLaneJournal.UndoChangeType, PropertyName = ProseLaneJournal.StateProperty,
                OldValue = state.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), NewValue = "{}", Timestamp = at.AddSeconds(1), ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undo = await service.UndoAsync(before);

            undo.SkippedRewritten.Should().Be(0, "the lane's own undo took its rewrite back, so no rewrite is in force");
            undo.RestoredWithheld.Should().Be(2);
        }

        [Fact]
        public async Task Apply_stops_at_the_batch_size_approved_first_and_records_no_run_while_failures_remain()
        {
            using var proven = ProseCheck.AssumeProven(ProseCheck.ChangedRange);
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
            result.Passed.Should().Be(1);
            result.Failed.Should().Be(1);
            result.Samples.Select(s => s.RecipeId).Should().Equal(copied);
            passing.Should().NotBe(copied);
        }

        [Fact]
        public async Task A_recipe_the_check_fails_on_is_counted_named_and_withheld_as_a_check_error_and_the_run_goes_on_to_record()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var broken = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var copied = await SeedAsync(db, CurationStatusEnum.Curated, CopiedStep);
            var passing = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
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
            result.Passed.Should().Be(1);
            result.Failed.Should().Be(2);
            result.FailedByReason.Should().ContainKey(ProseRecheckService.CheckErrorReason).WhoseValue.Should().Be(1);
            result.Withheld.Should().Be(2);
            result.Remaining.Should().Be(0);
            result.RunRecorded.Should().BeTrue();
            (await db.Recipes.Where(r => r.ProseWithheld).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync()).Should().Equal(new[] { broken, copied }.OrderBy(i => i));
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == ProseRecheckService.WithholdChangeType && a.EntityId == broken))
                .NewValue.Should().Contain("check-error: ArgumentOutOfRangeException");
            (await db.Recipes.SingleAsync(r => r.Id == passing)).ProseCheckVersion.Should().Be(ProseCheck.Version);
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == ProseRecheckService.RunChangeType)).NewValue.Should().EndWith("errored 1");
        }

        [Fact]
        public async Task A_dry_run_counts_a_recipe_the_check_fails_on_among_the_failures_left_shown()
        {
            await using var db = NewContext();
            await BuildStockAsync(db);
            var broken = await SeedAsync(db, CurationStatusEnum.Curated, Faithful);
            var service = Service(db);
            service.BeforeCheck = id =>
            {
                if (id == broken) throw new InvalidOperationException("boom");
            };

            var result = await service.RecheckAsync();

            result.Failed.Should().Be(1);
            result.Remaining.Should().Be(1);
            result.Samples.Should().ContainSingle().Which.Reasons.Should().Equal(ProseRecheckService.CheckErrorReason);
            (await db.Recipes.SingleAsync(r => r.Id == broken)).ProseWithheld.Should().BeFalse();
        }
    }
}
