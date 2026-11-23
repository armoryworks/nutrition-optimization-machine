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
    /// The prose check on the 200 recipes read against their source at staging checkpoints 2, 3 and 4
    /// (prose-check.tsv): at checkpoints 2 and 3, 40 the re-check failed and 40 it passed, at
    /// checkpoint 4, 40 it failed, kept as their whole stored texts, masked, with the verdict the check
    /// gave on staging. An <c>expect</c> of <c>passed</c> is met by a rewrite the check does not fail
    /// (passed or unsure), <c>unsure</c> by one it does not pass clean, and a reason by a finding with
    /// that reason that fails the rewrite, or only marks it unsure while the reason is a fact class not
    /// proven. No copied recipe may escape the copy rule, no faithful one may meet it, and at most 1
    /// in 20 failures may be false, on all the rows and on the rows the checkpoint operators read
    /// alone, whose verdicts were given before the check's limits were set, both with the committed
    /// proven classes and with every fact class taken as proven. The 35 verdicts the checks got wrong
    /// on staging each name the rule behind the error.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseCheckTests
    {
        public const string File = "prose-check.tsv";
        public const decimal MaximumFalseFailureRate = 0.05m;
        public const string OperatorSource = "agent-operator-";

        private readonly ITestOutputHelper _output;

        public GoldenProseCheckTests(ITestOutputHelper output) => _output = output;

        private static ProseCheck.Finding Check(GoldenRow row) =>
            ProseCheck.Check(
                new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                row.Value("lines")?.Split(" ¦ "));

        private static bool Meets(GoldenRow row, ProseCheck.Finding finding) => row["expect"] switch
        {
            "passed" => !finding.Failed,
            "unsure" => !finding.Passed,
            var reason => finding.Reasons.Contains(reason) && (finding.Failed || (ProseCheck.FactClasses.Contains(reason) && !finding.Proven.Contains(reason))),
        };

        private static bool IsCopy(GoldenRow row) => ProseCheck.CopyReasons.Contains(row["expect"]);

        private static List<GoldenRow> Judged() =>
            GoldenSet.Read(File).Rows.Where(r => r["status"] is GoldenSet.Ok or GoldenSet.Agent or GoldenSet.Known).ToList();

        [Fact]
        public void Each_read_recipe_gets_its_verdict()
        {
            var report = GoldenSet.Run(File, "status", row =>
            {
                var finding = Check(row);
                return new GoldenOutcome(Meets(row, finding), row["expect"], finding.ToString(),
                    new Dictionary<string, string> { ["expect"] = finding.Failed ? finding.FailReasons[0] : finding.Outcome });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void No_copied_recipe_passes_and_at_most_1_in_20_failures_is_false()
        {
            var rows = Judged();
            var findings = rows.ToDictionary(r => r.Id, Check);
            foreach (var set in rows.GroupBy(r => r["set"]))
                _output.WriteLine($"  {set.Key}: {string.Join(", ", set.GroupBy(r => findings[r.Id].Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))}");

            AssertBars("all read recipes", rows, findings);
            AssertBars("recipes the checkpoint operators read", rows.Where(ReadByOperator).ToList(), findings);
            using (ProseCheck.AssumeProven(ProseCheck.FactClasses.ToArray()))
            {
                var proven = rows.ToDictionary(r => r.Id, Check);
                AssertBars("all read recipes, every fact class proven", rows, proven);
                AssertBars("recipes the checkpoint operators read, every fact class proven", rows.Where(ReadByOperator).ToList(), proven);
            }
        }

        private static bool ReadByOperator(GoldenRow row) => row["source"].StartsWith(OperatorSource, System.StringComparison.Ordinal);

        private void AssertBars(string label, List<GoldenRow> rows, Dictionary<string, ProseCheck.Finding> findings)
        {
            var copies = rows.Where(IsCopy).ToList();
            var faithful = rows.Where(r => r["expect"] == "passed").ToList();
            var failures = rows.Where(r => findings[r.Id].Failed).ToList();
            var falseFailures = failures.Where(r => r["expect"] == "passed").Select(r => r.Id).ToList();
            var copiesPassed = copies.Where(r => !findings[r.Id].Failed).Select(r => r.Id).ToList();
            var outcomes = rows.GroupBy(r => findings[r.Id].Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
            _output.WriteLine($"check v{ProseCheck.Version}, {label} ({rows.Count}): {falseFailures.Count} false of {failures.Count} failures, {copiesPassed.Count} of {copies.Count} copies passed; {string.Join(", ", outcomes)}");

            copies.Should().NotBeEmpty();
            failures.Should().NotBeEmpty();
            copies.Where(r => !findings[r.Id].Copied).Select(r => r.Id).Should().BeEmpty($"the copy rule alone must catch every copied recipe ({label})");
            faithful.Where(r => findings[r.Id].Copied).Select(r => r.Id).Should().BeEmpty($"no faithful rewrite may meet the copy rule ({label})");
            copiesPassed.Should().BeEmpty($"a copied recipe must never pass the prose check ({label})");
            ((decimal)falseFailures.Count / failures.Count).Should().BeLessThanOrEqualTo(MaximumFalseFailureRate,
                $"at most 1 in 20 failures may be false ({label}); these faithful rewrites failed: {string.Join(", ", falseFailures)}");
        }

        [Fact]
        public void Each_verdict_the_checks_got_wrong_on_staging_names_its_rule_and_the_check_gets_it_right()
        {
            var wrong = GoldenSet.Read(File).Rows.Where(r => r["verdict"] is "failure wrong" or "copy").ToList();

            wrong.Where(r => r["v2"] != "-").Should().HaveCount(15, "checkpoint 2 read 9 false failures and 6 copies that passed check v2");
            wrong.Where(r => r["v3"] != "-").Should().HaveCount(12, "checkpoint 3 read 9 false failures and 3 copies that passed check v3");
            wrong.Where(r => r["v4"] != "-").Should().HaveCount(8, "checkpoint 4 read 8 false failures of check v4, all quantity rules");
            wrong.Where(r => r.Value("rule") == null).Select(r => r.Id).Should().BeEmpty("each wrong verdict names the rule behind it");
            wrong.Where(r => !Meets(r, Check(r))).Select(r => r.Id).Should().BeEmpty();
        }

        [Fact]
        public void A_weak_signal_alone_never_fails_a_read_recipe()
        {
            var failedOnWeakSignals = Judged()
                .Select(r => (Row: r, Finding: Check(r)))
                .Where(x => x.Finding.Faults.Count > 0 && x.Finding.Faults.All(f => ProseCheck.UnsureReasons.Contains(f.Reason)) && x.Finding.Failed)
                .Select(x => x.Row.Id)
                .ToList();
            var unsure = Judged().Count(r => Check(r).Unsure);

            failedOnWeakSignals.Should().BeEmpty();
            unsure.Should().BeGreaterThan(0, "the read recipes include verbs said another way, which the check marks unsure");
        }

        [Fact]
        public void No_verdict_depends_on_the_stock_index()
        {
            var changed = new List<string>();
            foreach (var row in GoldenSet.Read(File).Rows)
            {
                var texts = row["steps"].Split(" ¦ ").Concat(row["rewriteSteps"].Split(" ¦ "))
                    .Append(row.Value("description") ?? string.Empty).Append(row.Value("rewriteDescription") ?? string.Empty)
                    .Select(ProseQuantities.Prepare);
                var every = new ProseStockIndex(1, texts.SelectMany(t => ProsePhrases.Hashes(ProseCheck.CopyTokens(t)).Concat(ProsePhrases.Hashes(ProsePhrases.WordsOfPrepared(t)))));
                var stocked = ProseCheck.Check(
                    new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                    new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                    row.Value("lines")?.Split(" ¦ "), every);
                if (!stocked.Faults.SequenceEqual(Check(row).Faults)) changed.Add(row.Id);
            }

            changed.Should().BeEmpty("the copy rules count stock phrases like any other words, so the bars measured here hold for the stock index the re-check runs with");
        }
    }
}
