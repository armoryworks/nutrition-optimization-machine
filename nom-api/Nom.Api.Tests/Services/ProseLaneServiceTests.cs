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
    /// The reword prose lane against a database: with no lane state it decides in shadow and
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
            decisions.Should().OnlyContain(d => d.SignalsJson!.Contains("the butter gets whisked through the sauce"));
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
            steps[3].Description.Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[3]]);
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
        public async Task A_recipe_withheld_with_copied_sentences_is_reworded_as_a_whole_and_none_of_its_old_sentences_is_kept()
        {
            var rig = NewRig();
            var shownSteps = new[]
            {
                "Over medium heat, warm ½ tablespoon of the olive oil in a small saucepan, then cook the garlic in it for a minute or two until you can smell it.",
                "Pour in ¾ cup cream and ¾ teaspoon salt, then simmer for 7 to 10 minutes until slightly thickened.",
                "Whisk the butter through the sauce once you have lifted the pan off the heat.",
                "Before you serve it, leave the sauce to stand for a half hour.",
            };
            var id = await SeedAsync(rig.Db, steps: shownSteps);
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            recipe.ProseCopiedSentences = 1;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();
            var writer = new FakeWriter();

            var result = await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, new FakeJudge(), 25);

            result.Rewritten.Should().Be(1);
            writer.Reworded.Select(r => r.Step).Should().Equal(1, 2, 3, 4);
            var written = await RecipeAsync(rig.Db, id);
            written.ProseCopiedSentences.Should().Be(0);
            written.ProseWithheld.Should().BeFalse();
            written.RecipeSteps!.OrderBy(s => s.StepNumber).Select(s => s.Description).Should().Equal(GarlicSauce.Method.Select(m => GarlicSauce.Reworded[m]));
            (await rig.Db.LaneDecisions.SingleAsync()).Stratum.Should().Be("withheld-approved");
        }

        [Fact]
        public async Task Among_approved_withheld_recipes_the_most_copied_goes_first()
        {
            var rig = NewRig();
            var little = await SeedAsync(rig.Db);
            var much = await SeedAsync(rig.Db);
            var other = await SeedAsync(rig.Db, approved: false);
            foreach (var recipe in await rig.Db.Recipes.ToListAsync())
                recipe.ProseCopiedSentences = recipe.Id == little ? 1 : recipe.Id == much ? 3 : 9;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            await rig.Lane.SweepAsync(LaneMode.Shadow, Models, new FakeWriter(), new FakeJudge(), 25);

            (await rig.Db.LaneDecisions.OrderBy(d => d.Id).Select(d => d.Subject).ToListAsync())
                .Should().Equal(ProseLaneService.Subject(much), ProseLaneService.Subject(little), ProseLaneService.Subject(other));
        }

        [Fact]
        public async Task A_facts_only_recipe_withheld_with_a_count_of_copied_sentences_is_reworded_as_a_whole()
        {
            var rig = NewRig();
            var shownSteps = new[]
            {
                "Over medium heat, warm ½ tablespoon of the olive oil in a small saucepan, then cook the garlic in it for a minute or two until you can smell it.",
                "Pour in ¾ cup cream and ¾ teaspoon salt, then simmer for 7 to 10 minutes until slightly thickened.",
                "Whisk the butter through the sauce once you have lifted the pan off the heat.",
                "Before you serve it, leave the sauce to stand for a half hour.",
            };
            var id = await SeedAsync(rig.Db, steps: shownSteps);
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            recipe.ProseCopiedSentences = 1;
            recipe.SourceRights = SourceRightsRule.FactsOnly;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();
            var writer = new FakeWriter();

            await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, new FakeJudge(), 25);

            writer.Reworded.Select(r => r.Step).Should().HaveCountGreaterThan(1, "a facts-only recipe serves none of the publisher's wording, so every step is the lane's");
            (await rig.Db.LaneDecisions.SingleAsync()).Stratum.Should().NotBe("repair");
        }

        [Fact]
        public async Task Approved_withheld_recipes_go_first_then_other_withheld_then_quarantined_and_a_persons_edit_of_a_quarantined_one_is_left_alone()
        {
            var rig = NewRig();
            var quarantined = await SeedAsync(rig.Db, withheld: false, approved: false);
            var withheld = await SeedAsync(rig.Db, approved: false);
            var approved = await SeedAsync(rig.Db);
            var edited = await SeedAsync(rig.Db, withheld: false, approved: false, editedBy: 42);

            for (var i = 0; i < 4; i++) await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 1);

            (await rig.Db.LaneDecisions.OrderBy(d => d.Id).Select(d => d.Subject).ToListAsync())
                .Should().Equal(ProseLaneService.Subject(approved), ProseLaneService.Subject(withheld), ProseLaneService.Subject(quarantined));
            (await RecipeAsync(rig.Db, edited)).Should().Match<RecipeEntity>(r => r.ContainsSourceProse && r.ProseRewriteAttemptedAt == null);
        }

        [Theory]
        [InlineData(SourceRightsRule.PermissionNeeded)]
        [InlineData(SourceRightsRule.Forbidden)]
        public async Task A_recipe_of_a_permission_needed_or_forbidden_source_stays_withheld_and_out_of_the_queue(string rightsClass)
        {
            var rig = NewRig();
            var barred = await SeedAsync(rig.Db);
            var factsOnly = await SeedAsync(rig.Db, approved: false);
            foreach (var recipe in await rig.Db.Recipes.ToListAsync())
                recipe.SourceRights = recipe.Id == barred ? rightsClass : SourceRightsRule.FactsOnly;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            (await rig.Db.LaneDecisions.Select(d => d.Subject).ToListAsync()).Should().Equal(new[] { ProseLaneService.Subject(factsOnly) },
                "staging checkpoint 8: 2,449 recipes of two permission-needed sources were in the queue; they wait for the owner's decision on those sources");
            (await RecipeAsync(rig.Db, barred)).Should().Match<RecipeEntity>(r => r.ProseWithheld && r.ProseRewriteAttemptedAt == null);
        }

        [Fact]
        public async Task The_queue_read_lists_the_lanes_order_with_counts_by_tier_source_class_and_approval_and_the_recipes_kept_out()
        {
            var rig = NewRig();
            var quarantined = await SeedAsync(rig.Db, withheld: false, approved: false);
            var withheld = await SeedAsync(rig.Db, approved: false);
            var approved = await SeedAsync(rig.Db);
            var barred = await SeedAsync(rig.Db);
            foreach (var recipe in await rig.Db.Recipes.ToListAsync())
            {
                recipe.SourceRights = recipe.Id == barred ? SourceRightsRule.PermissionNeeded : recipe.Id == quarantined ? null : SourceRightsRule.FactsOnly;
                recipe.SourceSite = "example.org";
            }
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            var queue = await rig.Lane.QueueAsync(Models.Version, sample: 10);

            queue.Total.Should().Be(3);
            queue.First.Select(e => e.RecipeId).Should().Equal(approved, withheld, quarantined);
            queue.First.Select(e => e.Position).Should().Equal(1, 2, 3);
            queue.ByTier.Should().BeEquivalentTo(new Dictionary<string, int> { ["approved-withheld"] = 1, ["withheld"] = 1, ["quarantined"] = 1 });
            queue.BySourceClass.Should().BeEquivalentTo(new Dictionary<string, int> { [SourceRightsRule.FactsOnly] = 2, ["none"] = 1 });
            queue.ByApproved.Should().BeEquivalentTo(new Dictionary<string, int> { ["approved"] = 1, ["not-approved"] = 2 });
            queue.KeptOutByClass.Should().BeEquivalentTo(new Dictionary<string, int> { [SourceRightsRule.PermissionNeeded] = 1 });
            queue.LaneVersion.Should().Be(Models.Version);
            (await rig.Db.LaneDecisions.CountAsync()).Should().Be(0, "the queue read writes nothing");
        }

        [Fact]
        public async Task In_shadow_a_recipe_decided_under_this_version_is_not_taken_again_before_every_other_has_been_decided()
        {
            var rig = NewRig();
            var approved = await SeedAsync(rig.Db);
            var quarantined = await SeedAsync(rig.Db, withheld: false, approved: false);

            var sweeps = new List<ProseSweepResult>();
            for (var i = 0; i < 3; i++) sweeps.Add(await rig.Lane.SweepAsync(LaneMode.Shadow, Models, new FakeWriter(), new FakeJudge(), 1));

            (await rig.Db.LaneDecisions.OrderBy(d => d.Id).Select(d => d.Subject).ToListAsync())
                .Should().Equal(ProseLaneService.Subject(approved), ProseLaneService.Subject(quarantined));
            sweeps[2].Seen.Should().Be(0, "every recipe has one decision under this version; a repeat would count as another sample");
            (await ProseLaneService.EligibleQuery(rig.Db, Models.Version).CountAsync()).Should().Be(2, "the queue still holds both until they are written");

            var live = await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            live.Rewritten.Should().Be(2);
            (await RecipeAsync(rig.Db, approved)).ProseWithheld.Should().BeFalse();
        }

        [Fact]
        public async Task A_facts_only_recipe_with_a_step_a_person_edited_gets_its_whole_method_rewritten_not_only_its_description()
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db, editedBy: 42);
            var seeded = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            seeded.SourceRights = SourceRightsRule.FactsOnly;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            (await ProseLaneService.EligibleQuery(rig.Db, Models.Version, live: false).CountAsync()).Should().Be(1, "a withheld facts-only recipe is in the queue whoever edited it");
            var writer = new FakeWriter();
            var live = await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, new FakeJudge(), 25);

            live.DescriptionOnly.Should().Be(0);
            live.Rewritten.Should().Be(1);
            var recipe = await RecipeAsync(rig.Db, id);
            recipe.ProseWithheld.Should().BeFalse();
            recipe.RecipeSteps!.Where(st => !st.IsDeleted).Select(st => st.Description).Should().NotIntersectWith(OldSteps, "no untouched publisher step is shown again");
        }

        [Fact]
        public async Task A_withheld_recipe_whose_method_a_person_wrote_keeps_it_and_the_live_lane_replaces_only_its_copied_description()
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db, editedBy: 42);

            (await ProseLaneService.EligibleQuery(rig.Db, Models.Version).CountAsync()).Should().Be(1, "it is in the lane's queue on the Pipeline page");
            var shadow = await rig.Lane.SweepAsync(LaneMode.Shadow, Models, new FakeWriter(), new FakeJudge(), 25);
            shadow.Seen.Should().Be(0);
            (await RecipeAsync(rig.Db, id)).ProseWithheld.Should().BeTrue();

            var writer = new FakeWriter();
            var live = await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, new FakeJudge(), 25);

            live.DescriptionOnly.Should().Be(1);
            writer.Requests.Should().BeEmpty();
            var recipe = await RecipeAsync(rig.Db, id);
            recipe.ProseWithheld.Should().BeFalse();
            recipe.Description.Should().Be("Garlic Cream Sauce, made with heavy cream and butter.");
            recipe.RecipeSteps!.OrderBy(s => s.StepNumber).Select(s => s.Description).Should().Equal(OldSteps);
            recipe.RecipeSteps!.Should().OnlyContain(s => s.LastModifiedByPersonId == 42);
            (await rig.Db.AuditLogEntries.Where(a => a.EntityId == id).Select(a => a.PropertyName).ToListAsync()).Should().BeEquivalentTo("Description", "ProseState");
            (await ProseLaneService.EligibleQuery(rig.Db, Models.Version).CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_description_a_person_wrote_on_a_withheld_recipe_is_kept_when_the_method_is_rewritten()
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db);
            const string own = "Our garlic sauce for a quick weeknight pasta.";
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            recipe.Description = own;
            rig.Db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = ProseRecheckService.RecipeEntityType, EntityId = id, ChangeType = ProseRecheckService.DescriptionByPersonChangeType,
                PropertyName = "Description", OldValue = "The silkiest garlic sauce you will ever pour over pasta.", NewValue = own, Timestamp = DateTime.UtcNow, ChangedByPersonId = 42,
            });
            var other = await SeedAsync(rig.Db);
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            (await RecipeAsync(rig.Db, id)).Should().Match<RecipeEntity>(r => r.Description == own && !r.ProseWithheld);
            (await RecipeAsync(rig.Db, other)).Description.Should().Be("Garlic Cream Sauce, made with heavy cream and butter.");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task A_recipe_with_no_name_never_keeps_its_source_description_when_its_withhold_is_lifted(string name)
        {
            var rig = NewRig();
            var id = await SeedAsync(rig.Db);
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            recipe.Name = name;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            var result = await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            result.Rewritten.Should().Be(1);
            (await RecipeAsync(rig.Db, id)).Should().Match<RecipeEntity>(r => !r.ProseWithheld && string.IsNullOrEmpty(r.Description));
        }

        [Fact]
        public async Task An_auditor_flag_counts_as_an_error_when_the_auditor_confirms_it_whatever_the_judge_that_passed_the_rewrite_says()
        {
            var rig = NewRig();
            await SeedAsync(rig.Db);
            await rig.Lane.SweepAsync(LaneMode.Shadow, Models, new FakeWriter(), new FakeJudge { Confirms = _ => false }, 25);
            var auditor = new FakeAuditor { Faithful = _ => false, Confirms = _ => true };
            var audit = new ProseLaneAudit(rig.Db, auditor);

            var pass = await rig.Frame.AuditAsync(ProseLaneService.Lane, ProseLaneService.Tier, DateTime.UtcNow.AddDays(-1), new LaneSamplePlan(1, 1), audit, audit);

            pass.Errors.Should().Be(1);
            pass.Flagged.Should().Be(0);
            audit.Name.Should().Contain("phi4");
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
        public async Task A_recipe_the_lane_fails_on_is_counted_and_sent_to_the_back_of_the_queue_and_the_sweep_goes_on()
        {
            var rig = NewRig();
            await SeedAsync(rig.Db);
            await SeedAsync(rig.Db);
            var writer = new FakeWriter { Throw = new ArgumentOutOfRangeException("startIndex", "startIndex cannot be larger than length of string.") };

            var result = await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, new FakeJudge(), 25);

            result.Seen.Should().Be(2);
            result.Errors.Should().Be(2);
            result.Outage.Should().BeNull();
            (await rig.Db.Recipes.AsNoTracking().ToListAsync()).Should().OnlyContain(r => r.ProseRewriteAttemptedAt != null && r.ProseWithheld);
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

            var verdict = await rig.Lane.MeasureAsync(LaneMode.Shadow, Models, new FakeAuditor());

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
            var verdict = await rig.Lane.MeasureAsync(LaneMode.Live, Models, new FakeAuditor());

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
