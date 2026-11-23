using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The link re-check against the lines both staging checkpoints judged (link-recheck.tsv, both
    /// verdicts, and rule-given rows): no judged row is written against its judgement, a class may
    /// move or take back lines by rule only while it holds the bar (precision at least 95 % over at
    /// least 40 rows the checkpoints judged; rule-given rows must be right but do not count), the
    /// classes that write are the committed per-class record of these rows and nothing else, and a
    /// class below the bar writes nothing: its refutations stay as candidates for the model audit.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLinkRecheckTests
    {
        private static readonly Lazy<IReadOnlyList<RecheckRowOutcome>> Outcomes = new(() => LinkRecheckClassScorer.Decide(GoldenSet.Folder));

        private readonly ITestOutputHelper _output;

        public GoldenLinkRecheckTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void No_judged_row_is_written_against_its_judgement()
        {
            var byId = Outcomes.Value.ToDictionary(o => o.Row.Id);
            var report = GoldenSet.Run(LinkRecheckClassScorer.File, "status", row =>
            {
                var o = byId[row.Id];
                var pass = !o.Writes || o.Row.Judged == LinkRecheckClassScorer.Doubtful || o.Correct;
                var actual = $"{o.Verdict.Outcome}{(o.Verdict.Candidate is { } c ? " (candidate " + c + ")" : string.Empty)}: {o.Verdict.Reason}{(o.TargetId is { } t ? " -> #" + t : string.Empty)}";
                return new GoldenOutcome(pass, $"{row["judged"]} ({row["set"]} #{row["foodId"]} {row["lines"]})", actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            foreach (var line in LinkRecheckClassScorer.Report(Outcomes.Value)) _output.WriteLine(line);
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_class_that_writes_by_rule_holds_the_bar_on_its_judged_rows()
        {
            var scores = LinkRecheckClassScorer.Score(Outcomes.Value).ToDictionary(s => s.Class, StringComparer.Ordinal);
            foreach (var proven in LinkRecheckRules.ProvenClasses)
            {
                scores.Should().ContainKey(proven, $"the class {proven} writes by rule, so it needs judged rows");
                var score = scores[proven];
                score.AgentJudged.Should().BeGreaterThanOrEqualTo(LinkRecheckRules.ClassMinimumJudged,
                    $"the class {proven} writes by rule only over at least {LinkRecheckRules.ClassMinimumJudged} lines the checkpoints judged; rule-given rows do not count");
                score.AgentPrecision.Should().BeGreaterThanOrEqualTo(LinkRecheckRules.ClassPrecisionBar,
                    $"the class {proven} writes by rule only while {LinkRecheckRules.ClassPrecisionBar:P0} of its checkpoint-judged rows are right; take it out of LinkRecheckRules.ProvenClasses");
                score.Precision.Should().BeGreaterThanOrEqualTo(LinkRecheckRules.ClassPrecisionBar, $"the class {proven} must also get its rule-given rows right");
                score.MeetsBar.Should().BeTrue();
            }
        }

        [Fact]
        public void Rule_given_rows_never_lift_a_class_over_the_minimum_of_checkpoint_judged_rows()
        {
            new RecheckClassScore("prepared", 47, 47, 7, 7, 40, 40, 47, 141, false).MeetsBar
                .Should().BeFalse("47 rows reach the minimum only with 40 rule-given rows, which test take-backs by construction");
            new RecheckClassScore("alcohol", 60, 56, 40, 40, 20, 16, 56, 141, false).MeetsBar
                .Should().BeFalse("rule-given rows the class gets wrong hold it below the bar over all its rows");
            foreach (var score in LinkRecheckClassScorer.Score(Outcomes.Value))
            {
                _output.WriteLine($"{score.Class}: {score.AgentRight}/{score.AgentJudged} checkpoint-judged, {score.RuleRight}/{score.RuleJudged} rule-given");
                if (score.AgentJudged < LinkRecheckRules.ClassMinimumJudged)
                {
                    score.MeetsBar.Should().BeFalse($"{score.Class} has fewer than {LinkRecheckRules.ClassMinimumJudged} checkpoint-judged rows, whatever its rule rows");
                    LinkRecheckRules.Proven(score.Class).Should().BeFalse();
                }
            }
        }

        [Fact]
        public void The_proven_classes_are_the_committed_record_of_the_judged_rows_and_the_record_is_current()
        {
            var fresh = LinkRecheckClassScorer.BuildRecord(GoldenSet.Folder, Outcomes.Value, DateTime.UnixEpoch);
            var committed = System.Text.Json.JsonSerializer.Deserialize<LinkRecheckClassRecord.Record>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(GoldenSet.Folder, "scores", LinkRecheckClassRecord.FileName)), LinkRecheckClassRecord.Json)!;
            const string rerecord = "re-score and commit: dotnet run --project nom-api/Nom.Import -- --recheck-classes --record";

            committed.Should().BeEquivalentTo(fresh, o => o.Excluding(r => r.ScoredAtUtc),
                $"scores/{LinkRecheckClassRecord.FileName} must be what the judged rows of link-recheck.tsv give under these rules; a class is proven by the data, never by an edit of the record; {rerecord}");
            LinkRecheckClassRecord.Embedded().Should().BeEquivalentTo(committed, "the API is built with the committed record");
            LinkRecheckRules.ProvenProblem.Should().BeNull();
            LinkRecheckRules.ProvenClasses.Should().BeEquivalentTo(fresh.Proven,
                "the classes that write by rule are exactly the classes whose checkpoint-judged rows hold the bar");
            fresh.Proven.Should().BeEquivalentTo(LinkRecheckClassScorer.Score(Outcomes.Value).Where(s => s.MeetsBar).Select(s => s.Class));
        }

        [Fact]
        public void A_class_is_scored_on_every_row_it_refutes_whichever_classes_are_proven()
        {
            static IReadOnlyList<RecheckClassScore> ScoreUnder(params string[] proven)
            {
                using (LinkRecheckRules.AssumeProven(proven))
                    return LinkRecheckClassScorer.Score(LinkRecheckClassScorer.Decide(GoldenSet.Folder));
            }

            var none = ScoreUnder();
            foreach (var s in none) _output.WriteLine($"{s.Class}: {s.AgentRight}/{s.AgentJudged} checkpoint-judged, {s.RuleRight}/{s.RuleJudged} rule-given");
            var classes = none.Select(s => s.Class).ToArray();
            foreach (var proven in classes.Select(c => new[] { c }).Append(classes))
                ScoreUnder(proven).Should().BeEquivalentTo(none, o => o.Excluding(s => s.Proven),
                    $"a class is promoted on the rows it would write once proven, so no figure may change when {string.Join(", ", proven)} is proven");
            var record = LinkRecheckClassScorer.Score(Outcomes.Value);
            record.Should().BeEquivalentTo(none, o => o.Excluding(s => s.Proven), "the committed record is scored the same way, so re-recording settles at once");
        }

        [Fact]
        public void Every_checkpoint_row_is_an_operator_reading_by_an_agent()
        {
            var rows = LinkRecheckClassScorer.Rows(GoldenSet.Folder);
            rows.Where(r => r.Status == GoldenSet.Agent).Should().OnlyContain(r => GoldenSet.ByModel(r.Source),
                "the checkpoint lines were read by agent operators, not a person, and the source says so");
            rows.Where(r => r.Status != GoldenSet.Agent).Should().OnlyContain(r => r.Source.StartsWith("rule:", StringComparison.Ordinal),
                "only rule-given rows carry another status");
        }

        [Fact]
        public void A_checkpoint_row_keeps_only_its_foods_words_and_the_words_the_recheck_reads()
        {
            var word = new System.Text.RegularExpressions.Regex("[A-Za-zÀ-ÖØ-öø-ÿ]+");
            var pseudo = new System.Text.RegularExpressions.Regex("^q[bdfgjklmnprtvz][aeiou][bdfgjklmnprtvz][aeiou]r(s|es|ies|ly)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var allowed = GoldenSet.Read("link-recheck-words.tsv").Rows.Select(r => r["word"]).ToHashSet(StringComparer.Ordinal);
            var foods = ModelLanes.GateFoods(GoldenSet.Folder).ToDictionary(f => f.Id);
            IEnumerable<string> Words(string? text) => word.Matches(text ?? string.Empty).Select(m => m.Value.ToLowerInvariant());

            var leaks = new List<string>();
            foreach (var row in LinkRecheckClassScorer.Rows(GoldenSet.Folder).Where(r => r.Status == GoldenSet.Agent))
            {
                var own = new[] { (long?)row.FoodId, row.RightFood }.Where(id => id is { } v && foods.ContainsKey(v))
                    .SelectMany(id => Words(foods[id!.Value].Name + " " + foods[id.Value].Description)).ToHashSet(StringComparer.Ordinal);
                var kept = Words(row.Wording + " " + string.Join(" ", row.Lines)).Where(w => w.Length > 1 && !pseudo.IsMatch(w) && !own.Contains(w) && !allowed.Contains(w)).Distinct().ToList();
                if (kept.Count > 0) leaks.Add($"{row.Id}: {string.Join(", ", kept)}");
            }
            leaks.Should().BeEmpty("a checkpoint row from a scraped recipe keeps only the words of its food's catalog name and the words of link-recheck-words.tsv (units, function words, drink words, the gate's rule words and the words a row's decision reads); every other word is masked, and the readable rows stay outside the repository");
        }

        [Fact]
        public void A_class_below_the_bar_writes_nothing_and_hands_its_lines_to_the_model_audit()
        {
            var below = LinkRecheckClassScorer.Score(Outcomes.Value).Where(s => !s.MeetsBar).Select(s => s.Class).ToList();
            below.Should().NotBeEmpty("the checkpoints judged classes that cannot reach the bar by rule");
            below.Should().NotIntersectWith(LinkRecheckRules.ProvenClasses);
            var refuted = Outcomes.Value.Where(o => o.Class != null && below.Contains(o.Class)).ToList();
            refuted.Should().NotBeEmpty();
            refuted.Where(o => o.Writes).Select(o => o.Row.Id).Should().BeEmpty("a class below the bar never moves or takes back a line by rule");
            refuted.Should().OnlyContain(o => o.Verdict.Outcome == LinkRecheckRules.Unproven && o.Verdict.Candidate != null);
        }

        [Fact]
        public void Every_set_the_checkpoints_read_has_rows_judged_both_ways()
        {
            var rows = Outcomes.Value.Select(o => o.Row).ToList();
            foreach (var set in LinkRecheckRules.Sets)
            {
                rows.Where(r => r.Set == set && r.Status == GoldenSet.Agent).Select(r => r.Judged).Distinct()
                    .Should().Contain(new[] { LinkRecheckClassScorer.Wrong, LinkRecheckClassScorer.Right }.Where(j => set != LinkRecheckRules.Prepared || j == LinkRecheckClassScorer.Wrong),
                        $"the {set} set's judged lines hold both verdicts");
            }
        }
    }
}
