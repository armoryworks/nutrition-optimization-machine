using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The undo of the clean-room lane: it restores the description, the step rows and the prose
    /// state exactly as journaled whatever the step counts were, leaves a recipe withheld or
    /// quarantined again so its old text is never shown, skips a recipe a person edited or a later
    /// journal entry touched, and a repeat dry run finds nothing left.
    /// </summary>
    public class ProseRewriteUndoTests
    {
        private static readonly ProseLaneModels Models = new("qwen2.5:14b-instruct", "mistral-nemo", "phi4");

        private sealed record Fingerprint(string? Description, bool Withheld, bool Quarantined, int? CheckVersion, string? LaneVersion, int Attempts, string Steps);

        private static async Task<Fingerprint> FingerprintAsync(ApplicationDbContext db, long id)
        {
            var r = await db.Recipes.AsNoTracking().SingleAsync(x => x.Id == id);
            var steps = await db.RecipeSteps.AsNoTracking().Where(s => s.RecipeId == id).OrderBy(s => s.StepNumber).Select(s => s.StepNumber + ":" + s.Description).ToListAsync();
            return new Fingerprint(r.Description, r.ProseWithheld, r.ContainsSourceProse, r.ProseCheckVersion, r.ProseLaneVersion, r.ProseAttempts, string.Join("\n", steps));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(6)]
        public async Task Apply_then_undo_leaves_the_recipe_as_it_was_whatever_the_step_counts(int oldSteps)
        {
            var rig = ProseLaneServiceTests.NewRig();
            var steps = Enumerable.Range(1, oldSteps).Select(i => $"Old step {i}: heat and stir it until done.").ToArray();
            var id = await ProseLaneServiceTests.SeedAsync(rig.Db, steps: steps);
            var before = await FingerprintAsync(rig.Db, id);
            var since = DateTime.UtcNow.AddSeconds(-1);

            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);
            (await FingerprintAsync(rig.Db, id)).Should().NotBe(before);

            var dry = await rig.Undo.UndoAsync(since);
            var applied = await rig.Undo.UndoAsync(since, apply: true);
            var repeat = await rig.Undo.UndoAsync(since);

            dry.Should().Match<ProseRewriteUndoResult>(r => r.Restored == 1 && r.Remaining == 1 && !r.Applied);
            applied.Should().Match<ProseRewriteUndoResult>(r => r.Restored == 1 && r.Skipped == 0 && r.Remaining == 0);
            repeat.Restored.Should().Be(0);
            (await FingerprintAsync(rig.Db, id)).Should().Be(before);
            (await rig.Db.RecipeSteps.IgnoreQueryFilters().CountAsync(s => s.RecipeId == id && s.IsDeleted)).Should().Be(Math.Max(0, 4 - oldSteps) + 0);
            (await rig.Db.AuditLogEntries.SingleAsync(a => a.ChangeType == ProseLaneJournal.UndoChangeType)).EntityId.Should().Be(id);
        }

        [Fact]
        public async Task An_old_text_longer_than_one_journal_row_is_restored_whole()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var longStep = "Stir the sauce slowly and keep stirring. " + string.Concat(Enumerable.Repeat("Keep the heat low and stir the pot again. ", 220));
            var id = await ProseLaneServiceTests.SeedAsync(rig.Db, steps: new[] { longStep });
            var since = DateTime.UtcNow.AddSeconds(-1);
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            (await rig.Db.AuditLogEntries.CountAsync(a => a.PropertyName == "RecipeStep:1")).Should().Be(3);
            await rig.Undo.UndoAsync(since, apply: true);

            var step = await rig.Db.RecipeSteps.AsNoTracking().SingleAsync(s => s.RecipeId == id && !s.IsDeleted);
            step.Description.Should().Be(longStep);
        }

        [Fact]
        public async Task A_quarantined_recipe_goes_back_into_quarantine()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var id = await ProseLaneServiceTests.SeedAsync(rig.Db, withheld: false, approved: false);
            var since = DateTime.UtcNow.AddSeconds(-1);
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            await rig.Undo.UndoAsync(since, apply: true);

            (await rig.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == id)).Should().Match<RecipeEntity>(r => r.ContainsSourceProse && !r.ProseWithheld);
        }

        [Fact]
        public async Task A_recipe_a_person_edited_or_a_later_entry_touched_since_is_skipped_and_counted()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var edited = await ProseLaneServiceTests.SeedAsync(rig.Db);
            var touched = await ProseLaneServiceTests.SeedAsync(rig.Db);
            var since = DateTime.UtcNow.AddSeconds(-1);
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            var step = await rig.Db.RecipeSteps.FirstAsync(s => s.RecipeId == edited);
            step.Description = "My own words.";
            step.LastModifiedByPersonId = 42;
            rig.Db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = ProseRecheckService.RecipeEntityType, EntityId = touched, ChangeType = ProseRecheckService.WithholdChangeType,
                PropertyName = nameof(RecipeEntity.ProseWithheld), OldValue = "2", NewValue = "check 2 stock 1: copied-text", Timestamp = DateTime.UtcNow,
            });
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            var result = await rig.Undo.UndoAsync(since, apply: true);

            result.Should().Match<ProseRewriteUndoResult>(r => r.Restored == 0 && r.Skipped == 2 && r.SkippedEditedByPerson == 1 && r.SkippedChangedSince == 1);
            (await rig.Db.RecipeSteps.AsNoTracking().SingleAsync(s => s.RecipeId == edited && s.StepNumber == step.StepNumber)).Description.Should().Be("My own words.");
        }

        [Fact]
        public async Task An_undo_of_one_lane_version_leaves_the_rewrites_of_another()
        {
            var rig = ProseLaneServiceTests.NewRig();
            await ProseLaneServiceTests.SeedAsync(rig.Db);
            var since = DateTime.UtcNow.AddSeconds(-1);
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            (await rig.Undo.UndoAsync(since, "v2-000000000000")).Restored.Should().Be(0);
            (await rig.Undo.UndoAsync(since, Models.Version)).Restored.Should().Be(1);
        }

        [Fact]
        public async Task The_frame_undoes_the_lane_through_the_same_service()
        {
            var rig = ProseLaneServiceTests.NewRig();
            await ProseLaneServiceTests.SeedAsync(rig.Db);
            var since = DateTime.UtcNow.AddSeconds(-1);
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            var dry = await rig.Undo.UndoSinceAsync(ProseLaneService.Tier, since, false, default);
            var applied = await rig.Undo.UndoSinceAsync(ProseLaneService.Tier, since, true, default);

            rig.Undo.Lane.Should().Be(AiLanes.ProseRewrite);
            dry.Should().Be(new LaneUndoResult(1, 0, 1, dry.Note));
            applied.Should().Be(new LaneUndoResult(1, 0, 0, applied.Note));
            (await rig.Db.Recipes.AsNoTracking().SingleAsync()).ProseWithheld.Should().BeTrue();
        }
    }
}
