using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Platform;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The lane frame against a database: a lane starts in shadow and writes nothing, qualifies on
    /// an audited sample, pauses itself and undoes its window when its errors rise, journals every
    /// change, and stays paused across a restart because the gate reads the state from the database.
    /// </summary>
    public class LaneFrameServiceTests
    {
        private const string Lane = "test-lane";
        private const string Tier = "t1";
        private static readonly LanePolicy Policy = new(QualifyBar: 0.98, PauseBar: 0.03);

        private sealed class FakeAuditor : ILaneAuditor
        {
            public Func<LaneDecisionEntity, bool?> Wrong { get; set; } = _ => false;
            public string Model => "phi4";
            public Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) =>
                Task.FromResult(new LaneAuditAnswer(Wrong(decision)));
        }

        private sealed class FakeCheck : ILaneAuditCheck
        {
            public Func<LaneDecisionEntity, bool?> Confirms { get; set; } = _ => true;
            public string Name => "reader B blind";
            public Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) =>
                Task.FromResult(Confirms(decision));
        }

        private sealed class FakeUndo : ILaneUndo
        {
            public List<(string Tier, DateTime Since, bool Apply)> Calls { get; } = new();
            public bool Throw { get; set; }
            public string Lane => LaneFrameServiceTests.Lane;

            public Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
            {
                Calls.Add((tier, sinceUtc, apply));
                if (Throw) throw new InvalidOperationException("journal unreadable");
                return Task.FromResult(new LaneUndoResult(7, 1, 0));
            }
        }

        private sealed class Rig : IDisposable
        {
            public required ServiceProvider Provider { get; init; }
            public required FakeUndo Undo { get; init; }

            public async Task<T> With<T>(Func<ILaneFrameService, ApplicationDbContext, Task<T>> work)
            {
                using var scope = Provider.CreateScope();
                return await work(scope.ServiceProvider.GetRequiredService<ILaneFrameService>(), scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            }

            public AiLaneGate Gate()
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:QuietHours"] = "" }).Build();
                return new AiLaneGate(Provider.GetRequiredService<IServiceScopeFactory>(), configuration,
                    new AiHostMonitor(new NoClients(), configuration, NullLogger<AiHostMonitor>.Instance), NullLogger<AiLaneGate>.Instance);
            }

            public void Dispose() => Provider.Dispose();
        }

        private sealed class NoClients : System.Net.Http.IHttpClientFactory
        {
            public System.Net.Http.HttpClient CreateClient(string name) => new();
        }

        private static Rig NewRig(bool withUndo = true)
        {
            var dbName = Guid.NewGuid().ToString();
            var undo = new FakeUndo();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddScoped<IPlatformFeatureService, PlatformFeatureService>();
            services.AddScoped<ILaneFrameService, LaneFrameService>();
            if (withUndo) services.AddSingleton<ILaneUndo>(undo);
            return new Rig { Provider = services.BuildServiceProvider(), Undo = undo };
        }

        private static LaneEvaluateRequest Evaluate(string signature = "sig-1", params string[] pauseReasons) =>
            new(Lane, Tier, signature, Policy, AuditorScore.ExactRule, null, pauseReasons);

        private static async Task DecideAsync(Rig rig, int count, string signature = "sig-1", string stratum = "s")
        {
            await rig.With(async (frame, _) =>
            {
                var mode = await frame.ModeAsync(Lane, Tier);
                for (var i = 0; i < count; i++)
                {
                    await frame.RecordAsync(new LaneDecisionInput(Lane, Tier, $"wording:{i}", "food:1", mode, Written: true, signature, stratum));
                }
                return 0;
            });
        }

        private static Task<LaneAuditPassResult> AuditAllAsync(Rig rig, FakeAuditor auditor, FakeCheck check) =>
            rig.With((frame, _) => frame.AuditAsync(Lane, Tier, DateTime.UtcNow.AddDays(-1), new LaneSamplePlan(Minimum: 100_000), auditor, check, new Random(1)));

        private static async Task QualifyAsync(Rig rig)
        {
            await DecideAsync(rig, 200);
            await AuditAllAsync(rig, new FakeAuditor(), new FakeCheck());
            var verdict = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate()));
            verdict.Mode.Should().Be(LaneMode.Live);
        }

        [Fact]
        public async Task A_lane_with_no_state_is_in_shadow_and_its_decisions_are_not_written()
        {
            using var rig = NewRig();

            await DecideAsync(rig, 3);

            var (mode, written) = await rig.With(async (frame, db) => (await frame.ModeAsync(Lane, Tier), await db.LaneDecisions.CountAsync(d => d.Written)));
            mode.Should().Be(LaneMode.Shadow);
            written.Should().Be(0);
        }

        [Fact]
        public async Task A_shadow_tier_qualifies_on_its_audited_sample_and_journals_LaneQualify()
        {
            using var rig = NewRig();

            await DecideAsync(rig, 200);
            var audit = await AuditAllAsync(rig, new FakeAuditor(), new FakeCheck());
            var verdict = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate()));

            audit.Sampled.Should().Be(200);
            audit.Passed.Should().Be(200);
            verdict.Mode.Should().Be(LaneMode.Live);
            var (mode, journal) = await rig.With(async (frame, db) => (await frame.ModeAsync(Lane, Tier),
                await db.AuditLogEntries.Where(e => e.EntityType == LaneJournal.EntityType).Select(e => e.ChangeType).ToListAsync()));
            mode.Should().Be(LaneMode.Live);
            journal.Should().Equal(LaneJournal.Qualify);
        }

        [Fact]
        public async Task An_auditor_flag_counts_as_an_error_only_when_the_second_check_agrees()
        {
            using var rig = NewRig();
            await DecideAsync(rig, 10);

            var audit = await AuditAllAsync(rig,
                new FakeAuditor { Wrong = d => d.Subject is "wording:1" or "wording:2" or "wording:3" ? true : d.Subject == "wording:4" ? null : false },
                new FakeCheck { Confirms = d => d.Subject == "wording:1" ? true : d.Subject == "wording:2" ? false : null });

            audit.Should().Be(new LaneAuditPassResult(10, 10, 6, 1, 1, 2));
            var outcomes = await rig.With(async (_, db) => await db.LaneDecisions.ToDictionaryAsync(d => d.Subject, d => d.AuditOutcome));
            outcomes["wording:1"].Should().Be(LaneAuditOutcome.Error);
            outcomes["wording:2"].Should().Be(LaneAuditOutcome.Flagged);
            outcomes["wording:3"].Should().Be(LaneAuditOutcome.Unreadable);
            outcomes["wording:4"].Should().Be(LaneAuditOutcome.Unreadable);
        }

        [Fact]
        public async Task A_live_tier_whose_errors_rise_pauses_undoes_its_window_after_a_logged_dry_run_and_journals_both()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);
            var liveSince = await rig.With(async (_, db) => (await db.LaneStates.SingleAsync()).LastCleanAt!.Value);

            await DecideAsync(rig, 200);
            var n = 0;
            await AuditAllAsync(rig, new FakeAuditor { Wrong = _ => n++ % 20 == 0 }, new FakeCheck());
            var verdict = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate()));

            verdict.Mode.Should().Be(LaneMode.Paused);
            verdict.Figures.WindowErrors.Should().Be(10);
            rig.Undo.Calls.Should().Equal((Tier, liveSince, false), (Tier, liveSince, true));
            var journal = await rig.With(async (_, db) => await db.AuditLogEntries.OrderBy(e => e.Id).Select(e => new { e.ChangeType, e.NewValue }).ToListAsync());
            journal.Select(j => j.ChangeType).Should().Equal(LaneJournal.Qualify, LaneJournal.Pause, LaneJournal.Undo, LaneJournal.Undo);
            LaneJournal.Read<LaneJournal.UndoValue>(journal[2].NewValue)!.Applied.Should().BeFalse();
            LaneJournal.Read<LaneJournal.UndoValue>(journal[3].NewValue)!.Should().Be(new LaneJournal.UndoValue(true, liveSince, 7, 1, 0, null));
        }

        [Fact]
        public async Task A_pause_holds_across_a_restart_because_the_gate_reads_it_from_the_database()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);
            await rig.With((frame, _) => frame.EvaluateAsync(Evaluate("sig-1", "the auditor could not be reached")));

            var afterRestart = rig.Gate();

            (await afterRestart.WriteModeAsync(Lane, Tier, CancellationToken.None)).Should().Be(LaneMode.Paused);
            (await afterRestart.WriteModeAsync(Lane, "other-tier", CancellationToken.None)).Should().Be(LaneMode.Shadow);
        }

        [Fact]
        public async Task A_paused_whole_lane_pauses_every_tier_and_a_pause_switch_reads_as_paused()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);
            await rig.With(async (_, db) =>
            {
                db.LaneStates.Add(new LaneStateEntity { Lane = Lane, Tier = LaneTiers.Whole, Mode = LaneMode.Paused, Since = DateTime.UtcNow, WindowStart = DateTime.UtcNow });
                db.PlatformFeatures.Add(new PlatformFeatureEntity { Key = AiLanes.PauseKey("other-lane"), IsEnabled = true });
                return await db.SaveChangesAsync();
            });
            var gate = rig.Gate();

            (await gate.WriteModeAsync(Lane, Tier, CancellationToken.None)).Should().Be(LaneMode.Paused);
            (await gate.WriteModeAsync("other-lane", Tier, CancellationToken.None)).Should().Be(LaneMode.Paused);
        }

        [Fact]
        public async Task A_gate_that_cannot_read_the_state_lets_the_lane_decide_only_in_shadow()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("the database is restarting"));
            services.AddScoped<IPlatformFeatureService, PlatformFeatureService>();
            services.AddScoped<ILaneFrameService, LaneFrameService>();
            using var rig = new Rig { Provider = services.BuildServiceProvider(), Undo = new FakeUndo() };

            (await rig.Gate().WriteModeAsync(Lane, Tier, CancellationToken.None)).Should().Be(LaneMode.Shadow);
        }

        [Fact]
        public async Task A_paused_tier_resumes_on_a_clean_probation_sample_decided_after_the_pause()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);
            await rig.With((frame, _) => frame.EvaluateAsync(Evaluate("sig-1", "canary false accept")));

            var still = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate()));
            await DecideAsync(rig, 200);
            await AuditAllAsync(rig, new FakeAuditor(), new FakeCheck());
            var resumed = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate()));

            still.Mode.Should().Be(LaneMode.Paused);
            still.Figures.Audited.Should().Be(0);
            resumed.Mode.Should().Be(LaneMode.Live);
            resumed.Journal.Should().Be(LaneJournal.Resume);
        }

        [Fact]
        public async Task A_changed_signature_sends_a_live_tier_to_shadow_and_old_decisions_no_longer_count()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);

            var verdict = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate("sig-2")));
            var again = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate("sig-2")));

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Journal.Should().Be(LaneJournal.Shadow);
            again.Mode.Should().Be(LaneMode.Shadow);
            again.Figures.Audited.Should().Be(0);
            rig.Undo.Calls.Should().BeEmpty();
        }

        [Fact]
        public async Task A_failed_undo_leaves_the_tier_paused_and_journals_the_failure()
        {
            using var rig = NewRig();
            rig.Undo.Throw = true;
            await QualifyAsync(rig);

            var verdict = await rig.With((frame, _) => frame.EvaluateAsync(Evaluate("sig-1", "rule audit failed 3 times in 50")));

            verdict.Mode.Should().Be(LaneMode.Paused);
            var undo = await rig.With(async (_, db) => await db.AuditLogEntries.Where(e => e.ChangeType == LaneJournal.Undo).SingleAsync());
            LaneJournal.Read<LaneJournal.UndoValue>(undo.NewValue)!.Note.Should().Contain("journal unreadable");
            (await rig.With((frame, _) => frame.ModeAsync(Lane, Tier))).Should().Be(LaneMode.Paused);
        }

        [Fact]
        public async Task A_lane_with_no_undo_pauses_and_journals_that_nothing_was_undone()
        {
            using var rig = NewRig(withUndo: false);
            await QualifyAsync(rig);

            await rig.With((frame, _) => frame.EvaluateAsync(Evaluate("sig-1", "auditor unreachable")));

            var undo = await rig.With(async (_, db) => await db.AuditLogEntries.Where(e => e.ChangeType == LaneJournal.Undo).SingleAsync());
            LaneJournal.Read<LaneJournal.UndoValue>(undo.NewValue)!.Note.Should().Contain("no undo registered");
        }

        [Fact]
        public async Task A_refused_policy_is_not_evaluated()
        {
            using var rig = NewRig();

            var act = () => rig.With((frame, _) => frame.EvaluateAsync(Evaluate() with { Policy = new LanePolicy(0.98, 0.01) }));

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*would pause at once*");
        }

        [Fact]
        public async Task The_status_lists_each_tier_with_its_figures_and_whether_it_can_undo()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);

            var status = await rig.With((frame, _) => frame.StatusAsync());

            var row = status.Single();
            row.Mode.Should().Be(LaneMode.Live);
            row.CanUndo.Should().BeTrue();
            row.DecisionsLast24h.Should().Be(200);
            row.Figures!.Audited.Should().Be(200);
            row.Figures.QualifyBar.Should().Be(0.98);
        }

        [Fact]
        public async Task An_operator_undo_of_the_window_is_a_dry_run_unless_applied_and_only_the_apply_is_journaled()
        {
            using var rig = NewRig();
            await QualifyAsync(rig);

            var dry = await rig.With((frame, _) => frame.UndoWindowAsync(Lane, Tier, false, 10));
            var applied = await rig.With((frame, _) => frame.UndoWindowAsync(Lane, Tier, true, 10));
            var missing = await rig.With((frame, _) => frame.UndoWindowAsync("no-lane", Tier, true, 10));

            dry!.Applied.Should().BeFalse();
            applied!.Result.Undone.Should().Be(7);
            missing.Should().BeNull();
            var undo = await rig.With(async (_, db) => await db.AuditLogEntries.Where(e => e.ChangeType == LaneJournal.Undo).SingleAsync());
            undo.ChangedByPersonId.Should().Be(10);
        }
    }
}
