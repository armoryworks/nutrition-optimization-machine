using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The fact classes of the prose check against their judged rows of prose-check.tsv (column
    /// <c>classes</c>): every firing of a fact class is judged, a class fails a recipe only while it
    /// holds the bar (precision at least 95 % over at least 40 rows the checkpoints judged, a doubtful
    /// row counting against it, every rule-given row right and none counting toward the minimum), the
    /// classes that fail are the committed per-class record of these rows and nothing else, a record
    /// raised by hand proves nothing, and a class below the bar only marks its recipe unsure.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseCheckClassTests
    {
        private static readonly Lazy<IReadOnlyList<ProseRowOutcome>> Outcomes = new(() => ProseCheckClassScorer.Decide(GoldenSet.Folder));

        private readonly ITestOutputHelper _output;

        public GoldenProseCheckClassTests(ITestOutputHelper output) => _output = output;

        private static ProseCheckClassRecord.Record Committed() =>
            JsonSerializer.Deserialize<ProseCheckClassRecord.Record>(
                File.ReadAllText(Path.Combine(GoldenSet.Folder, "scores", ProseCheckClassRecord.FileName)), ProseCheckClassRecord.Json)!;

        [Fact]
        public void Every_fact_class_the_check_fires_on_a_read_recipe_is_judged_on_that_row()
        {
            foreach (var line in ProseCheckClassScorer.Report(Outcomes.Value)) _output.WriteLine(line);
            ProseCheckClassScorer.Unjudged(Outcomes.Value).Should().BeEmpty("a class is scored on every row it fires on; judge each new firing in the row's classes cell");
        }

        [Fact]
        public void The_proven_classes_are_the_committed_record_of_the_judged_rows_and_the_record_is_current()
        {
            var fresh = ProseCheckClassScorer.BuildRecord(GoldenSet.Folder, Outcomes.Value, DateTime.UnixEpoch);
            var committed = Committed();
            const string rerecord = "re-score and commit: dotnet run --project nom-api/Nom.Import -- --prose-classes --record";

            committed.Should().BeEquivalentTo(fresh, o => o.Excluding(r => r.ScoredAtUtc),
                $"scores/{ProseCheckClassRecord.FileName} must be what the judged rows of prose-check.tsv give under this check; a class is proven by the data, never by an edit of the record; {rerecord}");
            ProseCheckClassRecord.Embedded().Should().BeEquivalentTo(committed, "the API is built with the committed record");
            ProseCheck.ProvenProblem.Should().BeNull();
            ProseCheck.ProvenClasses.Should().BeEquivalentTo(fresh.Proven);
            fresh.Proven.Should().BeEquivalentTo(ProseCheckClassScorer.Score(Outcomes.Value).Where(s => s.MeetsBar).Select(s => s.Class));
        }

        [Fact]
        public void Every_class_that_fails_recipes_holds_the_bar_on_its_judged_rows()
        {
            var scores = ProseCheckClassScorer.Score(Outcomes.Value).ToDictionary(s => s.Class, StringComparer.Ordinal);
            foreach (var s in scores.Values) _output.WriteLine($"{s.Class}: {s.Right}/{s.Judged} ({s.Doubtful} doubtful), agent {s.AgentRight}/{s.AgentJudged}, rule {s.RuleRight}/{s.RuleJudged}, {(s.MeetsBar ? "fails recipes" : "unsure only")}");
            foreach (var proven in ProseCheck.ProvenClasses)
            {
                var score = scores[proven];
                score.AgentJudged.Should().BeGreaterThanOrEqualTo(ProseCheck.ClassMinimumJudged);
                score.AgentPrecision.Should().BeGreaterThanOrEqualTo(ProseCheck.ClassPrecisionBar);
                score.Precision.Should().BeGreaterThanOrEqualTo(ProseCheck.ClassPrecisionBar);
                score.RuleRight.Should().Be(score.RuleJudged);
            }
        }

        [Fact]
        public void A_class_below_the_bar_only_marks_its_recipes_unsure()
        {
            var below = ProseCheckClassScorer.Score(Outcomes.Value).Where(s => !s.MeetsBar).Select(s => s.Class).ToHashSet(StringComparer.Ordinal);
            below.Should().NotBeEmpty();
            var fired = Outcomes.Value.Where(o => o.Fired.Any(f => below.Contains(f.Class))).ToList();
            fired.Should().NotBeEmpty();
            foreach (var o in fired)
            {
                o.Finding.FailReasons.Should().NotIntersectWith(below, o.Row.Row.Id);
                o.Finding.UnprovenReasons.Should().BeEquivalentTo(o.Fired.Select(f => f.Class).Where(below.Contains), o.Row.Row.Id);
                if (!o.Finding.Faults.Any(f => ProseCheck.Fails(f.Reason, o.Finding.Proven))) o.Finding.Unsure.Should().BeTrue(o.Row.Row.Id);
            }
        }

        [Fact]
        public void A_doubtful_row_counts_against_its_class_and_a_wrong_rule_row_holds_it_below_the_bar()
        {
            new ProseClassScore("lost-quantity", 50, 40, 50, 40, 0, 0, 10, false).MeetsBar
                .Should().BeFalse("40 right and 10 doubtful firings are 80 %, not 100 %");
            new ProseClassScore("lost-quantity", 62, 61, 60, 60, 2, 1, 0, false).MeetsBar
                .Should().BeFalse("61 of 62 is 98.4 %, but a rule-given row the class gets wrong holds it below the bar");
            new ProseClassScore("lost-quantity", 80, 70, 40, 40, 40, 30, 0, false).MeetsBar.Should().BeFalse();
            new ProseClassScore("lost-quantity", 47, 47, 7, 7, 40, 40, 0, false).MeetsBar
                .Should().BeFalse("rule-given rows never lift a class over the minimum of checkpoint-judged rows");
            new ProseClassScore("lost-quantity", 42, 41, 40, 39, 2, 2, 1, false).MeetsBar.Should().BeTrue("39 of 40 is 97.5 %");
        }

        [Fact]
        public void A_record_raised_by_hand_proves_nothing()
        {
            ProseCheckClassRecord.Record Raised(int agentJudged, int agentRight, int ruleJudged, int ruleRight) => new()
            {
                CheckVersion = ProseCheck.Version,
                Bar = ProseCheck.ClassPrecisionBar,
                Minimum = ProseCheck.ClassMinimumJudged,
                Classes = new()
                {
                    new()
                    {
                        Class = ProseCheck.LostQuantity, Judged = agentJudged + ruleJudged, Right = agentRight + ruleRight,
                        AgentJudged = agentJudged, AgentRight = agentRight, RuleJudged = ruleJudged, RuleRight = ruleRight, MeetsBar = true,
                    },
                },
                Proven = new() { ProseCheck.LostQuantity },
            };

            var (proven, problem) = ProseCheckClassRecord.Evaluate(Raised(30, 30, 0, 0), ProseCheck.Version);
            proven.Should().BeEmpty("30 judged rows are below the minimum of 40, whatever the record says");
            problem.Should().Contain(ProseCheck.LostQuantity);
            ProseCheckClassRecord.Evaluate(Raised(60, 55, 0, 0), ProseCheck.Version).Proven.Should().BeEmpty("55 of 60 is below 95 %");
            ProseCheckClassRecord.Evaluate(Raised(60, 60, 2, 1), ProseCheck.Version).Proven.Should().BeEmpty("a rule-given row is wrong");
            var record = Raised(40, 40, 0, 0);
            record.Classes[0].Judged = 30;
            ProseCheckClassRecord.Evaluate(record, ProseCheck.Version).Proven.Should().BeEmpty("figures that do not add up prove nothing");
            var held = ProseCheckClassRecord.Evaluate(Raised(40, 40, 0, 0), ProseCheck.Version);
            held.Proven.Should().BeEquivalentTo(new[] { ProseCheck.LostQuantity });
            held.Problem.Should().BeNull();
            ProseCheckClassRecord.Evaluate(Raised(40, 40, 0, 0), ProseCheck.Version - 1).Proven.Should().BeEmpty("a record of another check version proves nothing");
            var listed = Raised(40, 40, 0, 0);
            listed.Classes[0].MeetsBar = false;
            ProseCheckClassRecord.Evaluate(listed, ProseCheck.Version).Proven.Should().BeEmpty();
            var notFact = Raised(40, 40, 0, 0);
            notFact.Classes[0].Class = ProseCheck.CopiedText;
            notFact.Proven = new() { ProseCheck.CopiedText };
            ProseCheckClassRecord.Evaluate(notFact, ProseCheck.Version).Proven.Should().BeEmpty("only a fact class is proven by the record");
        }

        [Fact]
        public void Every_judgement_of_a_class_is_right_false_or_doubtful()
        {
            var rows = ProseCheckClassScorer.Rows(GoldenSet.Folder);
            rows.SelectMany(r => r.Classes.Keys).Should().OnlyContain(c => ProseCheck.FactClasses.Contains(c));
            rows.Where(r => r.Classes.Count > 0).Should().OnlyContain(r => r.Status == GoldenSet.Agent || r.Status == GoldenSet.Ok);
        }
    }
}
