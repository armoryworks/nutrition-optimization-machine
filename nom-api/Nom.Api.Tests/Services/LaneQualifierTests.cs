using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Data.Curation;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The lane frame's arithmetic: Wilson bounds, the sample a bar needs, the audit correction,
    /// and the decision that qualifies, pauses and resumes a lane.
    /// </summary>
    public class LaneQualifierTests
    {
        private static readonly AuditorScore Exact = AuditorScore.ExactRule;
        private static readonly LanePolicy T2 = new(QualifyBar: 0.98, PauseBar: 0.03);
        private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static LaneQualifyInput Input(
            string mode, LaneCounts qualify, LaneCounts? recent = null, LanePolicy? policy = null, AuditorScore? auditor = null,
            string signature = "sig-1", bool changed = false, string? pausedSignature = null, string? golden = null, params string[] pauseReasons) =>
            new(mode, signature, changed, pausedSignature, policy ?? T2, auditor ?? Exact, golden, pauseReasons, qualify, recent ?? LaneCounts.None);

        private static LaneCounts Audited(int audited, int errors, int flagged = 0) =>
            new(audited, audited, audited, errors, flagged, Now.AddMinutes(-5));

        [Theory]
        [InlineData(0.99, 381)]
        [InlineData(0.98, 189)]
        [InlineData(0.97, 125)]
        public void The_sample_a_bar_needs_with_no_error_matches_the_plan_table(double bar, int needed)
        {
            LaneQualifier.MinimumSample(bar).Should().Be(needed);
            LaneQualifier.Wilson(needed, needed).Lower.Should().BeGreaterThanOrEqualTo(bar);
            LaneQualifier.Wilson(needed - 1, needed - 1).Lower.Should().BeLessThan(bar);
        }

        [Theory]
        [InlineData(0.98, 1)]
        [InlineData(0.97, 3)]
        public void At_300_audited_the_errors_a_bar_allows_match_the_plan_table(double bar, int allowed)
        {
            LaneQualifier.Wilson(300 - allowed, 300).Lower.Should().BeGreaterThanOrEqualTo(bar);
            LaneQualifier.Wilson(300 - allowed - 1, 300).Lower.Should().BeLessThan(bar);
        }

        [Fact]
        public void A_perfect_300_does_not_reach_a_99_percent_bar()
        {
            LaneQualifier.Wilson(300, 300).Lower.Should().BeApproximately(0.9874, 0.0005);
            var verdict = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(300, 0), policy: new LanePolicy(0.99, 0.015)), Now);

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Figures.NeededAudited.Should().Be(381);
            verdict.Reason.Should().Contain("300 of 381 audited");
        }

        [Fact]
        public void The_audit_correction_removes_false_flags_and_scales_by_sensitivity()
        {
            var score = new AuditorScore(KnownBad: 100, FlaggedBad: 80, KnownGood: 100, FlaggedGood: 2);

            var estimate = LaneQualifier.Correct(5, 100, score);

            estimate.Observed.Should().BeApproximately(0.05, 1e-9);
            estimate.Corrected.Should().BeApproximately((0.05 - 0.02) / (0.80 - 0.02), 1e-9);
            estimate.Lower.Should().BeLessThan(estimate.Corrected);
            estimate.Upper.Should().BeGreaterThan(estimate.Corrected);
        }

        [Fact]
        public void A_rule_check_is_not_corrected()
        {
            var estimate = LaneQualifier.Correct(3, 300, Exact);

            estimate.Corrected.Should().Be(estimate.Observed);
            (estimate.Lower, estimate.Upper).Should().Be(LaneQualifier.Wilson(3, 300));
        }

        [Fact]
        public void A_weaker_auditor_needs_a_larger_sample_and_an_exact_one_needs_the_plan_table()
        {
            var strong = new AuditorScore(400, 396, 400, 0);
            var weak = new AuditorScore(100, 70, 100, 0);

            LaneQualifier.NeededToQualify(T2, Exact).Should().Be(189);
            LaneQualifier.NeededToQualify(T2, strong).Should().BeGreaterThan(189);
            LaneQualifier.NeededToQualify(T2, weak).Should().BeGreaterThan(LaneQualifier.NeededToQualify(T2, strong)!.Value);
        }

        [Fact]
        public void A_check_that_false_flags_right_rows_never_needs_a_smaller_sample_than_an_exact_check()
        {
            var noisy = new AuditorScore(KnownBad: 500, FlaggedBad: 450, KnownGood: 1000, FlaggedGood: 30);
            var t1 = new LanePolicy(0.99, 0.015);

            LaneQualifier.NeededToQualify(t1, noisy).Should().BeGreaterThan(LaneQualifier.NeededToQualify(t1, Exact)!.Value);
            LaneQualifier.Correct(0, 200, noisy).Upper.Should().BeGreaterThanOrEqualTo(LaneQualifier.Correct(0, 200, Exact).Upper);
            LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(126, 0), policy: t1, auditor: noisy), Now).Mode.Should().Be(LaneMode.Shadow);
        }

        [Fact]
        public void A_check_that_false_flags_right_rows_does_not_hide_an_error_in_a_full_window()
        {
            var noisy = new AuditorScore(KnownBad: 500, FlaggedBad: 450, KnownGood: 1000, FlaggedGood: 30);
            var t1 = new LanePolicy(0.99, 0.015);
            var window = LaneQualifier.PauseWindow(t1, noisy);

            var verdict = LaneQualifier.Decide(Input(LaneMode.Live, Audited(1000, 0), recent: Audited(window, 1), policy: t1, auditor: noisy), Now);

            verdict.Mode.Should().Be(LaneMode.Paused);
            verdict.Undo.Should().BeTrue();
        }

        [Fact]
        public void A_shadow_tier_that_reports_a_fault_does_not_qualify()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(400, 0), policy: new LanePolicy(0.99, 0.015), pauseReasons: "canary false accept"), Now);

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Journal.Should().BeNull();
            verdict.Reason.Should().Contain("canary false accept");
        }

        [Fact]
        public void A_live_tier_whose_signature_changed_after_a_failing_window_pauses_and_undoes()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Live, LaneCounts.None, recent: Audited(253, 10), signature: "sig-2", changed: true), Now);

            verdict.Mode.Should().Be(LaneMode.Paused);
            verdict.Journal.Should().Be(LaneJournal.Pause);
            verdict.Undo.Should().BeTrue();
            verdict.Reason.Should().Contain("previous signature");
        }

        [Fact]
        public void A_policy_whose_pause_bar_a_qualified_lane_would_cross_is_refused()
        {
            new LanePolicy(0.98, 0.02).Problems().Should().ContainSingle().Which.Should().Contain("would pause at once");
            new LanePolicy(0.98, 0.03).Problems().Should().BeEmpty();
            new LanePolicy(1.0, 0.03).Problems().Should().NotBeEmpty();
        }

        [Fact]
        public void The_pause_window_grows_to_the_sample_a_clean_window_needs()
        {
            LaneQualifier.PauseWindow(new LanePolicy(0.99, 0.015, PauseWindow: 200), Exact).Should().Be(LaneQualifier.MinimumSample(1 - 0.015));
            LaneQualifier.PauseWindow(new LanePolicy(0.98, 0.03, PauseWindow: 200), Exact).Should().Be(200);
        }

        [Fact]
        public void A_shadow_tier_short_of_its_sample_stays_in_shadow()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(150, 0)), Now);

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Journal.Should().BeNull();
            verdict.Reason.Should().Contain("150 of 189 audited");
        }

        [Fact]
        public void A_shadow_tier_whose_lower_bound_reaches_the_bar_qualifies()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(300, 1)), Now);

            verdict.Mode.Should().Be(LaneMode.Live);
            verdict.Journal.Should().Be(LaneJournal.Qualify);
            verdict.Figures.PrecisionLower.Should().BeGreaterThanOrEqualTo(0.98);
        }

        [Fact]
        public void A_shadow_tier_with_too_many_errors_stays_in_shadow()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(300, 2)), Now);

            verdict.Mode.Should().Be(LaneMode.Shadow);
        }

        [Fact]
        public void Unconfirmed_flags_do_not_count_as_errors()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(300, 1, flagged: 9)), Now);

            verdict.Mode.Should().Be(LaneMode.Live);
            verdict.Figures.Flagged.Should().Be(9);
        }

        [Fact]
        public void Without_a_scored_audit_check_or_with_a_golden_problem_a_tier_never_qualifies()
        {
            LaneQualifier.Decide(new LaneQualifyInput(LaneMode.Shadow, "sig-1", false, null, T2, null, null, Array.Empty<string>(), Audited(1000, 0), LaneCounts.None), Now)
                .Mode.Should().Be(LaneMode.Shadow);
            LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(1000, 0), auditor: new AuditorScore(0, 0, 0, 0)), Now)
                .Reason.Should().Contain("no score");
            LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(1000, 0), golden: "no committed golden record for this signature"), Now)
                .Reason.Should().Contain("golden record");
        }

        [Fact]
        public void A_live_tier_pauses_when_the_error_upper_bound_of_a_full_window_is_above_the_bar()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Live, Audited(500, 2), recent: Audited(200, 3)), Now);

            verdict.Mode.Should().Be(LaneMode.Paused);
            verdict.Journal.Should().Be(LaneJournal.Pause);
            verdict.Undo.Should().BeTrue();
            verdict.Reason.Should().Contain("upper bound");
        }

        [Fact]
        public void A_live_tier_with_a_clean_full_window_stays_live_and_marks_it_clean()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Live, Audited(500, 1), recent: Audited(200, 0)), Now);

            verdict.Mode.Should().Be(LaneMode.Live);
            verdict.Journal.Should().BeNull();
            verdict.Clean.Should().BeTrue();
        }

        [Fact]
        public void A_window_still_filling_pauses_only_on_its_lower_bound_and_is_never_clean()
        {
            var few = LaneQualifier.Decide(Input(LaneMode.Live, Audited(300, 1), recent: Audited(40, 1)), Now);
            var many = LaneQualifier.Decide(Input(LaneMode.Live, Audited(300, 1), recent: Audited(40, 8)), Now);

            few.Mode.Should().Be(LaneMode.Live);
            few.Clean.Should().BeFalse();
            many.Mode.Should().Be(LaneMode.Paused);
            many.Reason.Should().Contain("lower bound");
        }

        [Fact]
        public void A_fault_the_lane_reports_pauses_a_live_tier_at_once()
        {
            var verdict = LaneQualifier.Decide(Input(LaneMode.Live, Audited(500, 0), recent: Audited(200, 0), pauseReasons: "the auditor could not be reached"), Now);

            verdict.Mode.Should().Be(LaneMode.Paused);
            verdict.Undo.Should().BeTrue();
            verdict.Reason.Should().Contain("could not be reached");
        }

        [Fact]
        public void A_changed_signature_or_a_lost_golden_record_sends_a_live_tier_to_shadow_without_an_undo()
        {
            var changed = LaneQualifier.Decide(Input(LaneMode.Live, LaneCounts.None, changed: true), Now);
            var golden = LaneQualifier.Decide(Input(LaneMode.Live, Audited(500, 0), recent: Audited(200, 0), golden: "golden record below floor"), Now);

            changed.Mode.Should().Be(LaneMode.Shadow);
            changed.Journal.Should().Be(LaneJournal.Shadow);
            changed.Undo.Should().BeFalse();
            golden.Mode.Should().Be(LaneMode.Shadow);
            golden.Undo.Should().BeFalse();
        }

        [Fact]
        public void A_paused_tier_resumes_only_when_a_sample_audited_since_the_pause_qualifies()
        {
            var probation = LaneQualifier.Decide(Input(LaneMode.Paused, Audited(100, 0)), Now);
            var resumed = LaneQualifier.Decide(Input(LaneMode.Paused, Audited(300, 0)), Now);

            probation.Mode.Should().Be(LaneMode.Paused);
            probation.Reason.Should().Contain("probation");
            resumed.Mode.Should().Be(LaneMode.Live);
            resumed.Journal.Should().Be(LaneJournal.Resume);
        }

        [Fact]
        public void A_tier_whose_policy_asks_for_a_new_signature_never_resumes_by_waiting()
        {
            var policy = T2 with { ResumeNeedsNewSignature = true };

            var same = LaneQualifier.Decide(Input(LaneMode.Paused, Audited(1000, 0), policy: policy, pausedSignature: "sig-1"), Now);
            var changed = LaneQualifier.Decide(Input(LaneMode.Paused, Audited(1000, 0), policy: policy, signature: "sig-2", pausedSignature: "sig-1"), Now);

            same.Mode.Should().Be(LaneMode.Paused);
            same.Reason.Should().Contain("new signature");
            changed.Mode.Should().Be(LaneMode.Live);
        }

        [Fact]
        public void A_three_percent_false_flag_rate_does_not_hold_a_right_lane_in_shadow_once_corrected()
        {
            var auditor = new AuditorScore(KnownBad: 400, FlaggedBad: 380, KnownGood: 1000, FlaggedGood: 30);
            var policy = new LanePolicy(0.97, 0.04);
            var audited = 2000;
            var falseFlags = (int)(audited * 0.03);

            var raw = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(audited, falseFlags), policy: policy), Now);
            var corrected = LaneQualifier.Decide(Input(LaneMode.Shadow, Audited(audited, falseFlags), policy: policy, auditor: auditor), Now);

            raw.Mode.Should().Be(LaneMode.Shadow);
            corrected.Mode.Should().Be(LaneMode.Live);
        }
    }
}
