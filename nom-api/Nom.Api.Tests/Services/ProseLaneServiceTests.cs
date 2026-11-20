using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The clean-room prose lane against a database: with no lane state it decides in shadow and
    /// lifts no withhold or quarantine (the old lane's clearing is gone); live, it writes and
    /// journals each rewrite; it takes approved withheld recipes first, skips a person's edits,
    /// gives a recipe up under its lane version and takes it again under a new one; canaries,
    /// gate yield and golden records hold or pause it, and a pause undoes its writes.
    /// </summary>
    public class ProseLaneServiceTests
    {
        private static readonly ProseLaneModels Models = new("qwen2.5:14b-instruct", "mistral-nemo", "phi4");

        private static readonly string[] OldSteps =
        {
            "Heat 1/2 tablespoon of the olive oil in a small saucepan over medium heat and cook the garlic for a minute or two until fragrant.",
            "Pour in 3/4 cup cream and 3/4 teaspoon salt, then simmer for 7 to 10 minutes until slightly thickened; take the pan off the heat, whisk in the butter and let the sauce stand for a half hour before serving.",
        };

        internal sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        internal sealed class Rig
        {
            public required ApplicationDbContext Db { get; init; }
            public required ProseLaneService Lane { get; init; }
            public required ILaneFrameService Frame { get; init; }
            public required ProseRewriteUndoService Undo { get; init; }
        }

        internal static Rig NewRig(ProseLaneGolden? golden = null)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            var undo = new ProseRewriteUndoService(db, new NoUser());
            var frame = new LaneFrameService(db, new ILaneUndo[] { undo }, NullLogger<LaneFrameService>.Instance);
            var lane = new ProseLaneService(db, frame, new ProseStockService(db, new NoUser()), NullLogger<ProseLaneService>.Instance,
                golden ?? new ProseLaneGolden(ProseLaneGolden.Default.Cases, new Dictionary<string, ProseLaneRecord>(), ProseLaneGolden.Default.Floors));
            return new Rig { Db = db, Lane = lane, Frame = frame, Undo = undo };
        }

        internal static async Task<long> SeedAsync(ApplicationDbContext db, bool withheld = true, bool approved = true, string[]? steps = null, string? jsonLd = null, long? editedBy = null)
        {
            var recipe = new RecipeEntity
            {
                Name = GarlicSauce.Dish,
                Description = "The silkiest garlic sauce you will ever pour over pasta.",
                ScrapedAtUtc = DateTime.UtcNow,
                LicenseStatus = RecipeLicenseStatus.Unknown,
                ProseWithheld = withheld,
                ContainsSourceProse = !withheld,
                ProseCheckVersion = withheld ? 2 : null,
                CurationStatusId = approved ? (long)CurationStatusEnum.Curated : (long)CurationStatusEnum.NonCurated,
                RecipeSteps = (steps ?? OldSteps).Select((text, i) => new RecipeStepEntity { StepNumber = i + 1, Description = text, LastModifiedByPersonId = editedBy }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe.Id, RawJsonLd = jsonLd ?? GarlicSauce.JsonLd() });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe.Id;
        }

        private static Task<RecipeEntity> RecipeAsync(ApplicationDbContext db, long id) =>
            db.Recipes.AsNoTracking().Include(r => r.RecipeSteps).SingleAsync(r => r.Id == id);

        private static async Task SetModeAsync(ApplicationDbContext db, string mode, DateTime? lastClean = null)
        {
            db.LaneStates.Add(new LaneStateEntity
            {
                Lane = ProseLaneService.Lane, Tier = ProseLaneService.Tier, Mode = mode, SignatureHash = Models.Version,
                Since = DateTime.UtcNow, WindowStart = DateTime.UtcNow.AddDays(-1), LastCleanAt = lastClean, CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        [Fact]
        public async Task A_lane_with_no_state_decides_in_shadow_and_lifts_no_withhold_or_quarantine()
        {
            var rig = NewRig();
            var withheld = await SeedAsync(rig.Db);
            var quarantined = await SeedAsync(rig.Db, withheld: false, approved: false);
            var mode = await rig.Frame.ModeAsync(ProseLaneService.Lane, ProseLaneService.Tier);

            var result = await rig.Lane.SweepAsync(mode, Models, new FakeWriter(), new FakeJudge(), 25);

            mode.Should().Be(LaneMode.Shadow);
            result.WouldRewrite.Should().Be(2);
            result.Rewritten.Should().Be(0);
            (await RecipeAsync(rig.Db, withheld)).Should().Match<RecipeEntity>(r => r.ProseWithheld && r.ProseRewriteAttemptedAt != null);
            var stillQuarantined = await RecipeAsync(rig.Db, quarantined);
            stillQuarantined.ContainsSourceProse.Should().BeTrue();
            stillQuarantined.RecipeSteps!.OrderBy(s => s.StepNumber).Select(s => s.Description).Should().Equal(OldSteps);
            (await rig.Db.AuditLogEntries.CountAsync()).Should().Be(0);
            var decisions = await rig.Db.LaneDecisions.ToListAsync();
            decisions.Should().HaveCount(2).And.OnlyContain(d => d.Writes && !d.Written && d.Decision == ProseLaneService.RewriteDecision && d.SignatureHash == Models.Version);
            decisions.Should().OnlyContain(d => d.SignalsJson!.Contains("Warm ½ tablespoon oil"));
        }

        [Fact]
        public async Task A_live_lane_writes_the_rewrite_lifts_the_withhold_and_journals_every_old_text_and_the_old_state()
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db);

            var result = await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            result.Rewritten.Should().Be(1);
            var recipe = await RecipeAsync(rig.Db, id);
            recipe.ProseWithheld.Should().BeFalse();
            recipe.ContainsSourceProse.Should().BeFalse();
            recipe.ProseCheckVersion.Should().Be(ProseCheck.Version);
            recipe.ProseLaneVersion.Should().Be(Models.Version);
            recipe.Description.Should().Be("Garlic Cream Sauce, made with heavy cream and butter.");
            var steps = recipe.RecipeSteps!.Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).ToList();
            steps.Should().HaveCount(4);
            steps[3].Description.Should().Be("Rest the sauce a half hour, then serve.");
            steps.Should().OnlyContain(s => s.LastModifiedByPersonId == SystemConstants.SystemPersonId);

            var journal = await rig.Db.AuditLogEntries.Where(a => a.EntityId == id).ToListAsync();
            journal.Should().OnlyContain(a => a.ChangeType == ProseRecheckService.RewriteChangeType && a.NewValue!.StartsWith($"[{Models.Writer} {Models.Version}] "));
            journal.Select(a => a.PropertyName).Should().BeEquivalentTo("Description", "RecipeStep:1", "RecipeStep:2", "RecipeStep:3", "RecipeStep:4", "ProseState");
            journal.Single(a => a.PropertyName == "RecipeStep:2").OldValue.Should().Be(OldSteps[1]);
            ProseLaneJournal.ReadState(journal.Single(a => a.PropertyName == "ProseState").OldValue)
                .Should().BeEquivalentTo(new ProseState(true, false, 2, null, 0, null, new[] { 1, 2 }));
            (await rig.Db.LaneDecisions.SingleAsync()).Written.Should().BeTrue();
        }

        [Fact]
        public async Task Approved_withheld_recipes_go_first_then_other_withheld_then_quarantined_and_a_persons_edit_is_left_alone()
        {
            var rig = NewRig();
            var quarantined = await SeedAsync(rig.Db, withheld: false, approved: false);
            var withheld = await SeedAsync(rig.Db, approved: false);
            var approved = await SeedAsync(rig.Db);
            var edited = await SeedAsync(rig.Db, editedBy: 42);

            for (var i = 0; i < 4; i++) await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 1);

            (await rig.Db.LaneDecisions.OrderBy(d => d.Id).Select(d => d.Subject).ToListAsync())
                .Should().Equal(ProseLaneService.Subject(approved), ProseLaneService.Subject(withheld), ProseLaneService.Subject(quarantined));
            (await RecipeAsync(rig.Db, edited)).Should().Match<RecipeEntity>(r => r.ProseWithheld && r.ProseRewriteAttemptedAt == null);
        }

        [Fact]
        public async Task A_recipe_every_attempt_fails_is_given_up_under_its_lane_version_and_taken_again_under_a_new_one()
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db);

            var first = await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge { Faithful = (_, _) => false }, 25);
            var again = await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);
            var newWriter = Models with { Writer = "qwen2.5:32b-instruct" };
            var next = await rig.Lane.SweepAsync(LaneMode.Live, newWriter, new FakeWriter(), new FakeJudge(), 25);

            first.GaveUp.Should().Be(1);
            first.GaveUpByClass.Should().ContainKey("judge:missing");
            again.Seen.Should().Be(0);
            next.Rewritten.Should().Be(1);
            (await rig.Db.LaneDecisions.OrderBy(d => d.Id).Select(d => d.Decision).FirstAsync()).Should().Be("exhausted:judge:missing");
            (await RecipeAsync(rig.Db, id)).ProseWithheld.Should().BeFalse();
        }

        [Fact]
        public async Task A_model_host_outage_ends_the_sweep_and_charges_no_recipe()
        {
            var rig = NewRig();
            var first = await SeedAsync(rig.Db);
            await SeedAsync(rig.Db);
            var writer = new FakeWriter { Throw = new HttpRequestException("connection refused") };

            var result = await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, new FakeJudge(), 25);

            result.Outage.Should().BeOfType<HttpRequestException>();
            result.Seen.Should().Be(1);
            (await rig.Db.Recipes.AsNoTracking().ToListAsync()).Should().OnlyContain(r => r.ProseRewriteAttemptedAt == null && r.ProseLaneVersion == null && r.ProseWithheld);
            (await rig.Db.LaneDecisions.CountAsync()).Should().Be(0);
            _ = first;
        }

        [Fact]
        public async Task Canaries_find_a_judge_that_lets_planted_faults_through_and_that_holds_the_lane()
        {
            var rig = NewRig();
            (await rig.Lane.CanariesDueAsync(Models.Version)).Should().BeTrue();

            var lax = await rig.Lane.RunCanariesAsync(Models, new FakeJudge());

            lax.Run.Should().Be(ProseLaneService.CanaryFaults + 1);
            lax.FalseAccepts.Should().HaveCount(ProseLaneService.CanaryFaults);
            (await rig.Lane.FaultsAsync(Models.Version)).Should().ContainSingle().Which.Should().StartWith("canary false accept");
            (await rig.Lane.CanariesDueAsync(Models.Version)).Should().BeFalse();

            var faithful = ProseLaneGolden.Default.Cases.Where(c => c.Faithful).Select(c => string.Join("|", c.Rewrite)).ToHashSet();
            var strict = await rig.Lane.RunCanariesAsync(Models, new FakeJudge { Faithful = (_, rewrite) => faithful.Contains(string.Join("|", rewrite)) });

            strict.FalseAccepts.Should().BeEmpty();
            strict.FalseRejects.Should().BeEmpty();
            (await rig.Lane.FaultsAsync(Models.Version)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_gate_yield_under_half_over_the_last_200_recipes_is_a_fault()
        {
            var rig = NewRig();
            for (var i = 0; i < ProseLaneService.YieldWindow; i++)
                await rig.Frame.RecordAsync(new LaneDecisionInput(ProseLaneService.Lane, ProseLaneService.Tier, "recipe:" + i, i % 3 == 0 ? "rewrite" : "exhausted:form", LaneMode.Live, i % 3 == 0, Models.Version));

            (await rig.Lane.FaultsAsync(Models.Version)).Should().ContainSingle().Which.Should().StartWith("gate yield 34 %");
            (await rig.Lane.FaultsAsync("another-version")).Should().BeEmpty();
        }

        [Fact]
        public async Task Without_current_golden_records_the_lane_stays_in_shadow_and_says_why()
        {
            var rig = NewRig();

            var verdict = await rig.Lane.MeasureAsync(LaneMode.Shadow, Models, new FakeJudge { Model = "phi4" }, new FakeJudge());

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Figures.GoldenProblem.Should().Contain("prose-rewrite has no committed golden score record").And.Contain("prose-judge has no committed golden score record");
            verdict.Figures.AuditorProblem.Should().NotBeNull();
        }

        [Fact]
        public async Task A_live_lane_pauses_on_a_canary_false_accept_and_undoes_its_rewrites_since_its_last_clean_window()
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db);
            await SetModeAsync(rig.Db, LaneMode.Live, DateTime.UtcNow.AddMinutes(-5));
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);
            (await RecipeAsync(rig.Db, id)).ProseWithheld.Should().BeFalse();

            await rig.Lane.RunCanariesAsync(Models, new FakeJudge());
            var verdict = await rig.Lane.MeasureAsync(LaneMode.Live, Models, new FakeJudge { Model = "phi4" }, new FakeJudge());

            verdict.Mode.Should().Be(LaneMode.Paused);
            verdict.Undo.Should().BeTrue();
            var recipe = await RecipeAsync(rig.Db, id);
            recipe.ProseWithheld.Should().BeTrue();
            recipe.RecipeSteps!.Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).Select(s => s.Description).Should().Equal(OldSteps);
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == "LaneUndo")).Should().Be(2);
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == ProseLaneJournal.UndoChangeType)).Should().Be(1);
            (await rig.Frame.ModeAsync(ProseLaneService.Lane, ProseLaneService.Tier)).Should().Be(LaneMode.Paused);
        }
    }
}
