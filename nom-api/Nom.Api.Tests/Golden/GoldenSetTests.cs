using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The golden loader on a scratch file whose rule is "the expected cell equals the upper-cased
    /// input": what passes, what regresses, what asks to be promoted, and what the update switch
    /// may and may not change.
    /// </summary>
    public sealed class GoldenSetTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "golden-" + Guid.NewGuid().ToString("N"));

        public GoldenSetTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private GoldenOptions Options(bool update = false, bool ci = false) => new(_folder, _folder, update, ci);

        private void Write(string rows, int? baseline = null)
        {
            File.WriteAllText(Path.Combine(_folder, "set.tsv"), "id\traw\tupper\tstatus\n" + rows);
            if (baseline is { } b) File.WriteAllText(Path.Combine(_folder, GoldenSet.BaselineFile), $"{{\"set.tsv:status\": {b}}}");
        }

        private static GoldenReport Run(GoldenOptions options) =>
            GoldenSet.Run("set.tsv", "status", row => GoldenSet.Compare(row, new Dictionary<string, string> { ["upper"] = row["raw"].ToUpperInvariant() }), options);

        private string Read(string file) => File.ReadAllText(Path.Combine(_folder, file));

        [Fact]
        public void Ok_rows_that_pass_and_known_rows_that_still_fail_within_the_baseline_pass()
        {
            Write("a\tsalt\tSALT\tok\nb\tpepper\twrong\tknown\nc\tskip\t*\t-\n", baseline: 1);

            var report = Run(Options());

            report.Failed.Should().BeFalse(report.Message());
            report.OkPassing.Should().Be(1);
            report.Known.Should().Be(1);
        }

        [Fact]
        public void An_ok_row_that_fails_is_listed_as_regressed()
        {
            Write("a\tsalt\tSUGAR\tok\n");

            var report = Run(Options());

            report.Failed.Should().BeTrue();
            report.Regressed.Should().ContainSingle().Which.Should().Contain("a (line 2): expected upper=SUGAR, got upper=SALT");
            FluentActions.Invoking(() => GoldenSet.Assert(report)).Should().Throw<GoldenSetFailure>().WithMessage("*REGRESSED*");
        }

        [Fact]
        public void A_known_row_that_now_passes_fails_the_run_until_it_is_promoted()
        {
            Write("a\tsalt\tSALT\tknown\n", baseline: 1);

            var report = Run(Options());

            report.NowPassing.Should().ContainSingle();
            FluentActions.Invoking(() => GoldenSet.Assert(report)).Should().Throw<GoldenSetFailure>().WithMessage("*NOW PASSES, promote it*");
        }

        [Fact]
        public void The_update_switch_promotes_a_passing_known_row_and_lowers_the_baseline()
        {
            Write("a\tsalt\tSALT\tknown\nb\tpepper\twrong\tknown\n", baseline: 2);

            var report = Run(Options(update: true));

            report.Failed.Should().BeTrue("a run with the update switch always ends red");
            Read("set.tsv").Should().Contain("a\tsalt\tSALT\tok").And.Contain("b\tpepper\twrong\tknown");
            GoldenSet.Baseline(_folder)["set.tsv:status"].Should().Be(1);
            Run(Options()).Failed.Should().BeFalse();
        }

        [Fact]
        public void The_update_switch_fills_only_blank_cells_of_new_rows_and_never_rewrites_a_judged_value()
        {
            Write("a\tsalt\t\tnew\nb\tpepper\twrong\tnew\nc\tdill\tDILL\tnew\n", baseline: 0);

            Run(Options(update: true));

            var text = Read("set.tsv");
            text.Should().Contain("a\tsalt\tSALT\treview", "a blank expected cell takes the current output and waits for a person");
            text.Should().Contain("b\tpepper\twrong\tknown", "a judged value is kept and the row is marked known");
            text.Should().Contain("c\tdill\tDILL\tok");
            GoldenSet.Baseline(_folder)["set.tsv:status"].Should().Be(0, "the switch never raises a baseline");
            Run(Options()).Unreviewed.Should().ContainSingle().Which.Should().StartWith("a (review)");
        }

        [Fact]
        public void An_agent_row_must_pass_like_an_ok_row_and_is_counted_apart()
        {
            Write("a\tsalt\tSALT\tagent\nb\tdill\tDILL\tok\nc\tpepper\tCUMIN\tagent\n");

            var report = Run(Options());

            report.Should().BeEquivalentTo(new { Ok = 1, OkPassing = 1, Agent = 2, AgentPassing = 1 }, o => o.ExcludingMissingMembers());
            report.Unreviewed.Should().BeEmpty();
            report.Regressed.Should().ContainSingle().Which.Should().Contain("c (line 4)").And.EndWith("(agent)");
            report.Summary.Should().Contain("1/2 agent rows pass (judged by a model reviewer, not yet by a person)");
        }

        [Fact]
        public void The_update_switch_marks_a_passing_row_a_model_judged_agent_and_never_ok()
        {
            File.WriteAllText(Path.Combine(_folder, "set.tsv"),
                "id\traw\tupper\tjudge\tstatus\na\tsalt\tSALT\tagent-review-2026-11-13\tnew\nb\tdill\tDILL\tperson\tnew\nc\tbay\tBAY\tagent-review-2026-11-13\tknown\n");
            File.WriteAllText(Path.Combine(_folder, GoldenSet.BaselineFile), "{\"set.tsv:status\": 1}");

            GoldenSet.Run("set.tsv", "status", row => GoldenSet.Compare(row, new Dictionary<string, string> { ["upper"] = row["raw"].ToUpperInvariant() }),
                Options(update: true), row => GoldenSet.ByModel(row["judge"]));

            var text = Read("set.tsv");
            text.Should().Contain("a\tsalt\tSALT\tagent-review-2026-11-13\tagent");
            text.Should().Contain("b\tdill\tDILL\tperson\tok");
            text.Should().Contain("c\tbay\tBAY\tagent-review-2026-11-13\tagent");
        }

        [Fact]
        public void The_update_switch_is_refused_under_ci()
        {
            Write("a\tsalt\t\tnew\n");

            FluentActions.Invoking(() => Run(Options(update: true, ci: true))).Should().Throw<InvalidOperationException>().WithMessage("*refused under CI*");
            Read("set.tsv").Should().Contain("a\tsalt\t\tnew");
        }
    }
}
